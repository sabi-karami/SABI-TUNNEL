import 'dart:async';

import 'package:flutter/material.dart';
import 'package:trusttunnel/common/extensions/context_extensions.dart';
import 'package:trusttunnel/common/localization/extensions/locale_enum_extension.dart';
import 'package:trusttunnel/common/localization/localization.dart';
import 'package:trusttunnel/common/logging/enum/logging_level.dart';
import 'package:trusttunnel/common/logging/enum/logging_security_type.dart';
import 'package:trusttunnel/data/model/server.dart';
import 'package:trusttunnel/data/model/vpn_configuration_log_level.dart';
import 'package:trusttunnel/data/model/vpn_state.dart';
import 'package:trusttunnel/feature/routing/routing/widgets/scope/routing_scope.dart';
import 'package:trusttunnel/feature/server/servers/widget/scope/servers_scope.dart';
import 'package:trusttunnel/feature/server/servers/widget/scope/servers_scope_aspect.dart';
import 'package:trusttunnel/feature/settings/app_logging/widgets/scope/app_logging_scope.dart';
import 'package:trusttunnel/feature/settings/excluded_routes/widgets/scope/excluded_routes_scope.dart';
import 'package:trusttunnel/feature/vpn/widgets/vpn_scope.dart';
import 'package:trusttunnel/widgets/firuze/firuze_palette.dart';
import 'package:trusttunnel/widgets/firuze/glass_card.dart';
import 'package:trusttunnel/widgets/firuze/tunnel_orb.dart';
import 'package:trusttunnel/widgets/firuze/tunnel_route.dart';

/// Home hero of the «شب فیروزه» edition: brand, the portal connect button,
/// live status with uptime, and the device → server tunnel card.
class TunnelHero extends StatefulWidget {
  final List<Server> servers;
  final VoidCallback onAddServer;

  const TunnelHero({
    super.key,
    required this.servers,
    required this.onAddServer,
  });

  @override
  State<TunnelHero> createState() => _TunnelHeroState();
}

class _TunnelHeroState extends State<TunnelHero> {
  VpnState _state = VpnState.disconnected;
  Server? _picked;
  DateTime? _connectedAt;
  Timer? _ticker;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final newState = VpnScope.vpnControllerOf(context).state;
    _picked = ServersScope.controllerOf(
      context,
      aspect: ServersScopeAspect.selectedServer,
    ).selectedServer;

    if (newState == VpnState.connected && _state != VpnState.connected) {
      _connectedAt = DateTime.now();
      _ticker?.cancel();
      _ticker = Timer.periodic(const Duration(seconds: 1), (_) {
        if (mounted) {
          setState(() {});
        }
      });
    } else if (newState != VpnState.connected) {
      _connectedAt = null;
      _ticker?.cancel();
      _ticker = null;
    }
    _state = newState;
  }

  @override
  void dispose() {
    _ticker?.cancel();
    super.dispose();
  }

  Server? get _target {
    final picked = _picked;
    if (picked != null) {
      for (final server in widget.servers) {
        if (server.id == picked.id) {
          return server;
        }
      }
    }

    return widget.servers.isEmpty ? null : widget.servers.first;
  }

  String get _uptime {
    final since = _connectedAt;
    if (since == null) {
      return '00:00:00';
    }
    final d = DateTime.now().difference(since);
    String two(int v) => v.toString().padLeft(2, '0');

    return '${two(d.inHours)}:${two(d.inMinutes % 60)}:${two(d.inSeconds % 60)}';
  }

  Future<void> _onPortalTap() async {
    final target = _target;
    if (target == null) {
      widget.onAddServer();

      return;
    }

    final vpn = VpnScope.vpnControllerOf(context, listen: false);
    if (_picked?.id == target.id && _state != VpnState.disconnected) {
      await vpn.stop();

      return;
    }

    await _connect(target);
  }

  Future<void> _connect(Server server) async {
    final serversController = ServersScope.controllerOf(context, listen: false);
    final controller = VpnScope.vpnControllerOf(context, listen: false);
    final excludedRoutes = ExcludedRoutesScope.controllerOf(context, listen: false).excludedRoutes;
    final loggingController = AppLoggingScope.controllerOf(context, listen: false);
    if (loggingController.loading) {
      return;
    }

    final logLevel = switch (loggingController.securityType) {
      LoggingSecurityType.stripped => VpnConfigurationLogLevel.error,
      LoggingSecurityType.full => switch (loggingController.loggingLevel) {
        LoggingLevel.defaultLevel => VpnConfigurationLogLevel.info,
        LoggingLevel.debug => VpnConfigurationLogLevel.debug,
      },
    };

    final routingProfile = RoutingScope.controllerOf(context, listen: false).routingList.firstWhere(
      (element) => element.id == server.serverData.routingProfileId,
    );

    serversController.pickServer(server.id);
    await controller.start(
      server: server,
      routingProfile: routingProfile,
      excludedRoutes: excludedRoutes,
      logLevel: logLevel,
    );
  }

  @override
  Widget build(BuildContext context) {
    final ln = context.ln;
    final target = _target;
    final hasServer = target != null;
    final state = target != null && _picked?.id == target.id ? _state : VpnState.disconnected;
    final connected = state == VpnState.connected;
    final busy = !connected && state != VpnState.disconnected;

    final (title, tone) = switch (state) {
      VpnState.connected => (ln.heroConnected, Firuze.turquoise),
      VpnState.connecting => (ln.heroConnecting, Firuze.gold),
      VpnState.waitingForNetwork => (ln.heroWaitingForNetwork, Firuze.gold),
      VpnState.recovering || VpnState.waitingForRecovery => (ln.heroRecovering, Firuze.gold),
      VpnState.disconnected => (hasServer ? ln.heroDisconnected : ln.heroNoServerTitle, Firuze.text),
    };

    final String subtitle;
    if (target == null) {
      subtitle = ln.heroNoServerDescription;
    } else if (connected) {
      subtitle = '${ln.heroUptime}  ${Firuze.faDigits(_uptime)}';
    } else if (busy) {
      subtitle = target.serverData.name;
    } else {
      subtitle = ln.heroTapToConnect;
    }

    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
      child: Column(
        children: [
          _Brand(edition: ln.heroEdition),
          const SizedBox(height: 6),
          TunnelOrb(
            state: state,
            enabled: hasServer,
            onTap: _onPortalTap,
            semanticLabel: connected ? ln.disconnect : ln.connect,
          ),
          AnimatedSwitcher(
            duration: const Duration(milliseconds: 300),
            child: Text(
              title,
              key: ValueKey(title),
              textAlign: TextAlign.center,
              style: context.textTheme.headlineSmall?.copyWith(
                fontWeight: FontWeight.w900,
                color: tone,
                shadows: [
                  Shadow(color: tone.withValues(alpha: 0.45), blurRadius: 18),
                ],
              ),
            ),
          ),
          const SizedBox(height: 4),
          Text(
            subtitle,
            textAlign: TextAlign.center,
            style: context.textTheme.bodyMedium?.copyWith(color: Firuze.muted),
          ),
          const SizedBox(height: 18),
          if (target != null)
            _RouteCard(
              server: target,
              connected: connected,
              busy: busy,
            )
          else
            _EmptyCard(onAddServer: widget.onAddServer),
        ],
      ),
    );
  }
}

class _Brand extends StatelessWidget {
  final String edition;

  const _Brand({required this.edition});

  @override
  Widget build(BuildContext context) => Row(
    mainAxisAlignment: MainAxisAlignment.center,
    children: [
      ShaderMask(
        blendMode: BlendMode.srcIn,
        shaderCallback: (bounds) => Firuze.auroraGradient.createShader(bounds),
        child: Text(
          'SABI TUNNEL',
          textDirection: TextDirection.ltr,
          style: context.textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w900),
        ),
      ),
      const SizedBox(width: 10),
      Container(
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 3),
        decoration: ShapeDecoration(
          shape: const StadiumBorder(side: BorderSide(color: Color(0x80F5C76B))),
          color: Firuze.gold.withValues(alpha: 0.12),
        ),
        child: Text(
          edition,
          style: context.textTheme.labelMedium?.copyWith(
            color: Firuze.gold,
            fontWeight: FontWeight.w700,
          ),
        ),
      ),
    ],
  );
}

class _RouteCard extends StatelessWidget {
  final Server server;
  final bool connected;
  final bool busy;

  const _RouteCard({
    required this.server,
    required this.connected,
    required this.busy,
  });

  @override
  Widget build(BuildContext context) {
    final ln = context.ln;
    final data = server.serverData;

    return GlassCard(
      glowColor: connected ? Firuze.turquoise : null,
      padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
      child: Column(
        children: [
          Row(
            children: [
              _Endpoint(
                icon: Icons.smartphone_rounded,
                label: ln.heroYou,
                color: connected ? Firuze.turquoise : Firuze.text,
              ),
              Expanded(
                child: Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 8),
                  child: TunnelRoute(active: connected, busy: busy),
                ),
              ),
              _Endpoint(
                icon: Icons.dns_rounded,
                label: data.name,
                color: connected ? Firuze.gold : Firuze.text,
              ),
            ],
          ),
          const SizedBox(height: 12),
          Wrap(
            alignment: WrapAlignment.center,
            spacing: 8,
            runSpacing: 8,
            children: [
              _Chip(
                icon: Icons.bolt_rounded,
                label: data.vpnProtocol.localized(context),
                color: Firuze.violet,
              ),
              _Chip(
                icon: Icons.public_rounded,
                label: data.ipAddress,
                color: Firuze.turquoise,
                ltr: true,
              ),
              if (connected)
                _Chip(
                  icon: Icons.lock_rounded,
                  label: ln.heroEncrypted,
                  color: Firuze.gold,
                ),
            ],
          ),
        ],
      ),
    );
  }
}

class _Endpoint extends StatelessWidget {
  final IconData icon;
  final String label;
  final Color color;

  const _Endpoint({
    required this.icon,
    required this.label,
    required this.color,
  });

  @override
  Widget build(BuildContext context) => SizedBox(
    width: 84,
    child: Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        Container(
          width: 44,
          height: 44,
          decoration: BoxDecoration(
            shape: BoxShape.circle,
            color: color.withValues(alpha: 0.12),
            border: Border.all(color: color.withValues(alpha: 0.5)),
            boxShadow: [BoxShadow(color: color.withValues(alpha: 0.25), blurRadius: 14)],
          ),
          child: Icon(icon, size: 22, color: color),
        ),
        const SizedBox(height: 6),
        Text(
          label,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          textAlign: TextAlign.center,
          style: context.textTheme.labelMedium?.copyWith(color: Firuze.text),
        ),
      ],
    ),
  );
}

class _Chip extends StatelessWidget {
  final IconData icon;
  final String label;
  final Color color;
  final bool ltr;

  const _Chip({
    required this.icon,
    required this.label,
    required this.color,
    this.ltr = false,
  });

  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
    decoration: ShapeDecoration(
      shape: StadiumBorder(side: BorderSide(color: color.withValues(alpha: 0.4))),
      color: color.withValues(alpha: 0.1),
    ),
    child: Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(icon, size: 14, color: color),
        const SizedBox(width: 6),
        Flexible(
          child: Text(
            label,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            textDirection: ltr ? TextDirection.ltr : null,
            style: context.textTheme.labelMedium?.copyWith(color: Firuze.text),
          ),
        ),
      ],
    ),
  );
}

class _EmptyCard extends StatelessWidget {
  final VoidCallback onAddServer;

  const _EmptyCard({required this.onAddServer});

  @override
  Widget build(BuildContext context) => GlassCard(
    glowColor: Firuze.violet,
    padding: const EdgeInsets.all(18),
    child: Column(
      children: [
        const Icon(Icons.add_link_rounded, color: Firuze.gold, size: 30),
        const SizedBox(height: 8),
        Text(
          context.ln.serversEmptyDescription,
          textAlign: TextAlign.center,
          style: context.textTheme.bodyMedium,
        ),
        const SizedBox(height: 14),
        FilledButton.icon(
          onPressed: onAddServer,
          icon: const Icon(Icons.add_rounded),
          label: Text(context.ln.addServer),
        ),
      ],
    ),
  );
}
