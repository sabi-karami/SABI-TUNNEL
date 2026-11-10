#!/usr/bin/env python3
"""Create (or reuse) a dual-stack IPv4+IPv6 TrustTunnel server on AWS EC2.

Idempotent: resources are found by the tag  Project=trusttunnel-stack, Name=<name>.

    pip install boto3
    python infra/aws_deploy.py --region us-east-2 --name tt-oh --pubkey ~/.ssh/id_ed25519.pub

Prints a JSON summary (instance id, IPv4, IPv6).
"""
import argparse
import json
import time

import boto3

PROJECT = "trusttunnel-stack"


def tags(name, kind):
    return [{"ResourceType": kind, "Tags": [{"Key": "Name", "Value": name},
                                             {"Key": "Project", "Value": PROJECT}]}]


def find(items, key="Tags", name=None):
    for it in items:
        t = {x["Key"]: x["Value"] for x in it.get(key, [])}
        if t.get("Project") == PROJECT and (name is None or t.get("Name") == name):
            return it
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--region", default="us-east-2")
    ap.add_argument("--name", default="tt-oh")
    ap.add_argument("--instance-type", default="t4g.micro")
    ap.add_argument("--disk-gb", type=int, default=12)
    ap.add_argument("--pubkey", required=True, help="path to an SSH public key")
    ap.add_argument("--cidr", default="10.77.0.0/16")
    a = ap.parse_args()

    s = boto3.Session(region_name=a.region)
    ec2 = s.client("ec2")
    arch = "arm64" if a.instance_type.split(".")[0].endswith("g") else "amd64"

    # VPC with an Amazon-provided IPv6 /56
    vpc = find(ec2.describe_vpcs(Filters=[{"Name": "tag:Project", "Values": [PROJECT]}])["Vpcs"], name=a.name)
    if not vpc:
        vpc = ec2.create_vpc(CidrBlock=a.cidr, AmazonProvidedIpv6CidrBlock=True,
                             TagSpecifications=tags(a.name, "vpc"))["Vpc"]
        ec2.get_waiter("vpc_available").wait(VpcIds=[vpc["VpcId"]])
        ec2.modify_vpc_attribute(VpcId=vpc["VpcId"], EnableDnsHostnames={"Value": True})
    vpc_id = vpc["VpcId"]
    for _ in range(30):
        v = ec2.describe_vpcs(VpcIds=[vpc_id])["Vpcs"][0]
        assoc = [x for x in v.get("Ipv6CidrBlockAssociationSet", [])
                 if x["Ipv6CidrBlockState"]["State"] == "associated"]
        if assoc:
            v6_block = assoc[0]["Ipv6CidrBlock"]
            break
        time.sleep(2)

    igw = find(ec2.describe_internet_gateways(Filters=[{"Name": "attachment.vpc-id", "Values": [vpc_id]}])["InternetGateways"])
    if not igw:
        igw = ec2.create_internet_gateway(TagSpecifications=tags(a.name, "internet-gateway"))["InternetGateway"]
        ec2.attach_internet_gateway(InternetGatewayId=igw["InternetGatewayId"], VpcId=vpc_id)
    igw_id = igw["InternetGatewayId"]

    # Pick an AZ that offers the instance type
    azs = [o["Location"] for o in ec2.describe_instance_type_offerings(
        LocationType="availability-zone",
        Filters=[{"Name": "instance-type", "Values": [a.instance_type]}])["InstanceTypeOfferings"]]
    azs.sort()
    subnet = find(ec2.describe_subnets(Filters=[{"Name": "vpc-id", "Values": [vpc_id]}])["Subnets"])
    if not subnet:
        sub6 = v6_block.replace("00::/56", "00::/64")
        subnet = ec2.create_subnet(VpcId=vpc_id, CidrBlock=a.cidr.replace("0.0/16", "1.0/24"),
                                   Ipv6CidrBlock=sub6, AvailabilityZone=azs[0],
                                   TagSpecifications=tags(a.name, "subnet"))["Subnet"]
        ec2.modify_subnet_attribute(SubnetId=subnet["SubnetId"], AssignIpv6AddressOnCreation={"Value": True})
    subnet_id = subnet["SubnetId"]

    rt = ec2.describe_route_tables(Filters=[{"Name": "vpc-id", "Values": [vpc_id]},
                                            {"Name": "association.main", "Values": ["true"]}])["RouteTables"][0]
    have = {r.get("DestinationCidrBlock") or r.get("DestinationIpv6CidrBlock") for r in rt["Routes"]}
    if "0.0.0.0/0" not in have:
        ec2.create_route(RouteTableId=rt["RouteTableId"], DestinationCidrBlock="0.0.0.0/0", GatewayId=igw_id)
    if "::/0" not in have:
        ec2.create_route(RouteTableId=rt["RouteTableId"], DestinationIpv6CidrBlock="::/0", GatewayId=igw_id)

    sgs = ec2.describe_security_groups(Filters=[{"Name": "vpc-id", "Values": [vpc_id]},
                                                {"Name": "group-name", "Values": [a.name]}])["SecurityGroups"]
    if sgs:
        sg_id = sgs[0]["GroupId"]
    else:
        sg_id = ec2.create_security_group(GroupName=a.name, Description="TrustTunnel endpoint",
                                          VpcId=vpc_id, TagSpecifications=tags(a.name, "security-group"))["GroupId"]
        any_v = {"IpRanges": [{"CidrIp": "0.0.0.0/0"}], "Ipv6Ranges": [{"CidrIpv6": "::/0"}]}
        ec2.authorize_security_group_ingress(GroupId=sg_id, IpPermissions=[
            {"IpProtocol": "tcp", "FromPort": 443, "ToPort": 443, **any_v},
            {"IpProtocol": "udp", "FromPort": 443, "ToPort": 443, **any_v},
            {"IpProtocol": "tcp", "FromPort": 80, "ToPort": 80, **any_v},
            {"IpProtocol": "tcp", "FromPort": 22, "ToPort": 22, **any_v},
            {"IpProtocol": "icmp", "FromPort": -1, "ToPort": -1, "IpRanges": [{"CidrIp": "0.0.0.0/0"}]},
            {"IpProtocol": "icmpv6", "FromPort": -1, "ToPort": -1, "Ipv6Ranges": [{"CidrIpv6": "::/0"}]},
        ])
        try:
            ec2.authorize_security_group_egress(GroupId=sg_id, IpPermissions=[
                {"IpProtocol": "-1", "Ipv6Ranges": [{"CidrIpv6": "::/0"}]}])
        except ec2.exceptions.ClientError:
            pass

    key_name = f"{a.name}-key"
    if not ec2.describe_key_pairs(Filters=[{"Name": "key-name", "Values": [key_name]}])["KeyPairs"]:
        ec2.import_key_pair(KeyName=key_name, PublicKeyMaterial=open(a.pubkey, "rb").read())

    inst = None
    for r in ec2.describe_instances(Filters=[{"Name": "tag:Project", "Values": [PROJECT]},
                                             {"Name": "tag:Name", "Values": [a.name]},
                                             {"Name": "instance-state-name",
                                              "Values": ["pending", "running", "stopping", "stopped"]}])["Reservations"]:
        inst = r["Instances"][0]
    if not inst:
        ami = s.client("ssm").get_parameter(
            Name=f"/aws/service/canonical/ubuntu/server/24.04/stable/current/{arch}/hvm/ebs-gp3/ami-id")["Parameter"]["Value"]
        inst = ec2.run_instances(
            ImageId=ami, InstanceType=a.instance_type, MinCount=1, MaxCount=1, KeyName=key_name,
            NetworkInterfaces=[{"DeviceIndex": 0, "SubnetId": subnet_id, "Groups": [sg_id],
                                "Ipv6AddressCount": 1, "AssociatePublicIpAddress": False}],
            BlockDeviceMappings=[{"DeviceName": "/dev/sda1",
                                  "Ebs": {"VolumeSize": a.disk_gb, "VolumeType": "gp3", "DeleteOnTermination": True}}],
            CreditSpecification={"CpuCredits": "standard"},
            MetadataOptions={"HttpTokens": "required", "HttpEndpoint": "enabled"},
            TagSpecifications=tags(a.name, "instance") + tags(a.name, "volume"),
        )["Instances"][0]
    iid = inst["InstanceId"]
    ec2.get_waiter("instance_running").wait(InstanceIds=[iid])

    addrs = ec2.describe_addresses(Filters=[{"Name": "tag:Project", "Values": [PROJECT]},
                                            {"Name": "tag:Name", "Values": [a.name]}])["Addresses"]
    eip = addrs[0] if addrs else ec2.allocate_address(Domain="vpc", TagSpecifications=tags(a.name, "elastic-ip"))
    if eip.get("InstanceId") != iid:
        ec2.associate_address(AllocationId=eip["AllocationId"], InstanceId=iid)

    inst = ec2.describe_instances(InstanceIds=[iid])["Reservations"][0]["Instances"][0]
    ipv6 = [x["Ipv6Address"] for ni in inst["NetworkInterfaces"] for x in ni.get("Ipv6Addresses", [])]
    print(json.dumps({"region": a.region, "name": a.name, "instance_id": iid,
                      "instance_type": a.instance_type, "az": inst["Placement"]["AvailabilityZone"],
                      "ipv4": eip["PublicIp"], "ipv6": ipv6[0] if ipv6 else None,
                      "vpc_id": vpc_id, "security_group": sg_id}, indent=2))


if __name__ == "__main__":
    main()
