// SABI TUNNEL: the panel's default servers and self-updates.
//
// The panel publishes a feed (/app/v1/feed) with the configs the admin marked "in apps" and the newest app builds.
// Default servers are added, updated (the user's protocol and routing choices stay) and removed in step with the
// panel; servers the user added are never touched, except that a hand-added copy of a default server (same host and
// user) is taken over instead of being duplicated. A deleted default server comes back with the next sync. Changes to
// the server in use wait until the VPN is disconnected.
import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/services.dart';
import 'package:flutter/widgets.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:trusttunnel/data/model/server.dart';
import 'package:trusttunnel/data/model/server_data.dart';
import 'package:trusttunnel/data/model/vpn_state.dart';
import 'package:trusttunnel/data/repository/deep_link_repository.dart';
import 'package:trusttunnel/data/repository/server_repository.dart';
import 'package:trusttunnel/data/repository/vpn_repository.dart';

class _Item {
  final String server;
  final String hash;
  final bool adopted;

  const _Item(this.server, this.hash, this.adopted);

  Map<String, Object> toJson() => {'server': server, 'hash': hash, 'adopted': adopted};

  static _Item? fromJson(Object? j) {
    if (j is! Map<String, dynamic>) return null;
    final s = j['server'];
    if (s is! String || s.isEmpty) return null;
    final h = j['hash'];
    return _Item(s, h is String ? h : '', j['adopted'] == true);
  }
}

class _Config {
  final String id;
  final String link;
  final String hash;

  const _Config(this.id, this.link, this.hash);
}

class SabiSync with WidgetsBindingObserver {
  SabiSync({
    required ServerRepository servers,
    required DeepLinkRepository deepLinks,
    required VpnRepository vpn,
    required VoidCallback onChanged,
  }) : _servers = servers,
       _deepLinks = deepLinks,
       _vpn = vpn,
       _onChanged = onChanged;

  static const appKey = 'st1-Vb7Qm2Kx9Rf4Lp8Zt3Hw';
  static const builtInFeeds = <String>[
    'https://sabitunnel.sahebkarami.ir/app/v1/feed',
    'https://ttpanel-oh.sahebkarami.ir/app/v1/feed',
  ];

  /// Build number stamped by CI (--dart-define=SABI_BUILD=...); 0 = local build, which never updates itself.
  static const build = int.fromEnvironment('SABI_BUILD');

  static const _channel = MethodChannel('sabi/update');
  static const _indexKey = 'sabi_feed_index_v1';
  static const _feedsKey = 'sabi_feed_urls_v1';
  static const _offerKey = 'sabi_update_offer_v1';

  static final Set<String> _managed = <String>{};

  /// Whether the server is one of the panel's default servers.
  static bool isDefault(String? serverId) => serverId != null && _managed.contains(serverId);

  final ServerRepository _servers;
  final DeepLinkRepository _deepLinks;
  final VpnRepository _vpn;
  final VoidCallback _onChanged;

  Timer? _timer;
  bool _syncing = false;
  bool _deferred = false;
  bool _foreground = true;
  bool _disposed = false;
  DateTime _next = DateTime.fromMillisecondsSinceEpoch(0);
  int _fails = 0;
  Map<String, dynamic>? _lastFeed;
  String _lastSource = '';

  bool _updating = false;
  int _stagedCode = 0;
  String? _stagedPath;

  void start() {
    WidgetsBinding.instance.addObserver(this);
    unawaited(_loadManaged());
    _timer = Timer.periodic(const Duration(seconds: 60), (_) => unawaited(_tick()));
    Timer(const Duration(seconds: 2), () => unawaited(sync()));
  }

  void dispose() {
    _disposed = true;
    _timer?.cancel();
    WidgetsBinding.instance.removeObserver(this);
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    _foreground = state == AppLifecycleState.resumed;
    // back on screen: sync again if the last one is more than 5 minutes old
    if (_foreground && DateTime.now().isAfter(_next.subtract(const Duration(minutes: 25)))) {
      unawaited(sync());
    }
  }

  Future<void> _tick() async {
    if (_disposed || _syncing) return;
    if (DateTime.now().isAfter(_next) || (_deferred && !await _vpnBusy())) {
      await sync();
      return;
    }
    final feed = _lastFeed;
    if (feed != null) await _update(feed, _lastSource);
  }

  Future<bool> _vpnBusy() async {
    try {
      final s = await _vpn.requestState().timeout(const Duration(seconds: 5));
      return s != VpnState.disconnected;
    } catch (_) {
      return false;
    }
  }

  Future<void> _loadManaged() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      final index = _readIndex(prefs);
      _managed
        ..clear()
        ..addAll(index.values.map((e) => e.server));
      if (_managed.isNotEmpty && !_disposed) _onChanged();
    } catch (_) {}
  }

  static Map<String, _Item> _readIndex(SharedPreferences prefs) {
    final index = <String, _Item>{};
    try {
      final raw = prefs.getString(_indexKey);
      final Object? j = raw == null ? null : jsonDecode(raw);
      if (j is Map<String, dynamic>) {
        for (final e in j.entries) {
          final it = _Item.fromJson(e.value);
          if (it != null) index[e.key] = it;
        }
      }
    } catch (_) {}
    return index;
  }

  static bool _goodUrl(String u) {
    final x = Uri.tryParse(u);
    return x != null && x.scheme == 'https' && x.host.isNotEmpty;
  }

  /// Fetches the feed and brings the default servers in line with it. False when no feed could be read.
  Future<bool> sync() async {
    if (_syncing || _disposed) return false;
    _syncing = true;
    try {
      final prefs = await SharedPreferences.getInstance();
      final feed = await _fetch(prefs);
      if (feed == null) {
        _fails++;
        _next = DateTime.now().add(Duration(minutes: _fails * 2 > 10 ? 10 : _fails * 2));
        return false;
      }
      _fails = 0;
      _next = DateTime.now().add(const Duration(minutes: 30));
      _lastFeed = feed;
      final changed = await _apply(feed, prefs);
      if (changed && !_disposed) _onChanged();
      unawaited(_update(feed, _lastSource));
      return true;
    } catch (e) {
      debugPrint('[sabi] sync: $e');
      _next = DateTime.now().add(const Duration(minutes: 5));
      return false;
    } finally {
      _syncing = false;
    }
  }

  Future<Map<String, dynamic>?> _fetch(SharedPreferences prefs) async {
    final saved = prefs.getStringList(_feedsKey) ?? const <String>[];
    final urls = <String>{...saved, ...builtInFeeds}.where(_goodUrl).take(6).toList();
    for (final u in urls) {
      final client = HttpClient()
        ..connectionTimeout = const Duration(seconds: 10)
        ..userAgent = 'SabiTunnel-Android/$build';
      try {
        final uri = Uri.parse(u);
        final query = Map<String, String>.of(uri.queryParameters)..['k'] = appKey;
        final req = await client.getUrl(uri.replace(queryParameters: query)).timeout(const Duration(seconds: 12));
        final res = await req.close().timeout(const Duration(seconds: 15));
        if (res.statusCode != 200) {
          await res.drain<void>();
          continue;
        }
        final body = await res.transform(utf8.decoder).join().timeout(const Duration(seconds: 20));
        final Object? j = jsonDecode(body);
        if (j is Map<String, dynamic> && j['v'] == 1 && j['configs'] is List<dynamic>) {
          _lastSource = u;
          final f = j['feeds'];
          if (f is List<dynamic>) {
            final feeds = f.whereType<String>().where(_goodUrl).take(4).toList();
            if (feeds.isNotEmpty) await prefs.setStringList(_feedsKey, feeds);
          }
          return j;
        }
      } catch (e) {
        debugPrint('[sabi] feed $u: $e');
      } finally {
        client.close(force: true);
      }
    }
    return null;
  }

  Future<ServerData> _parse(String link) {
    final t = link.trim();
    final uri = Uri.parse(t);
    final normalized = uri.hasQuery ? Uri(scheme: 'tt', host: '', query: uri.query).toString() : t;
    return _deepLinks.parseDataFromLink(deepLink: normalized);
  }

  /// The link's server with the choices the user made for the existing entry.
  static ServerData _keep(ServerData fresh, ServerData old) => fresh.copyWith(
    vpnProtocol: old.vpnProtocol,
    routingProfileId: old.routingProfileId,
    selected: old.selected,
  );

  Future<bool> _apply(Map<String, dynamic> feed, SharedPreferences prefs) async {
    final configs = <_Config>[];
    final raw = feed['configs'];
    if (raw is List<dynamic>) {
      for (final c in raw) {
        if (c is! Map<String, dynamic>) continue;
        final id = c['id'];
        final link = c['link'];
        final hash = c['hash'];
        if (id is! String || link is! String) continue;
        if (id.isEmpty || !link.trim().startsWith('tt://')) continue;
        configs.add(_Config(id, link, hash is String ? hash : ''));
      }
    }
    final feedIds = configs.map((c) => c.id).toSet();
    final index = _readIndex(prefs);
    final busy = await _vpnBusy();
    var all = await _servers.getAllServers();
    final byId = <String, Server>{for (final s in all) s.id: s};
    final owned = index.values.map((e) => e.server).toSet();
    var changed = false;
    var deferred = false;

    for (final c in configs) {
      final it = index[c.id];
      final cur = it == null ? null : byId[it.server];
      if (it != null && cur != null && it.hash == c.hash) continue;
      if (cur != null && busy && cur.serverData.selected) {
        deferred = true;
        continue;
      }
      final ServerData data;
      try {
        data = await _parse(c.link);
      } catch (e) {
        debugPrint('[sabi] ${c.id}: $e');
        continue;
      }
      if (it != null && cur != null) {
        await _servers.setNewServer(id: cur.id, request: _keep(data, cur.serverData));
        index[c.id] = _Item(cur.id, c.hash, it.adopted);
        changed = true;
        continue;
      }
      // new, or deleted by the user: take over a hand-added copy (same host and user) instead of duplicating it
      Server? twin;
      for (final s in all) {
        if (!owned.contains(s.id) && s.serverData.domain == data.domain && s.serverData.username == data.username) {
          twin = s;
          break;
        }
      }
      if (twin != null) {
        if (busy && twin.serverData.selected) {
          deferred = true;
          continue;
        }
        await _servers.setNewServer(id: twin.id, request: _keep(data, twin.serverData));
        index[c.id] = _Item(twin.id, c.hash, true);
        owned.add(twin.id);
      } else {
        final added = await _servers.addNewServer(request: data);
        index[c.id] = _Item(added.id, c.hash, false);
        owned.add(added.id);
      }
      changed = true;
    }

    // removed in the panel: delete what this sync added; taken-over servers were the user's own and stay
    for (final id in index.keys.toList()) {
      if (feedIds.contains(id)) continue;
      final it = index[id];
      if (it == null) continue;
      final cur = byId[it.server];
      if (cur != null && !it.adopted) {
        if (busy && cur.serverData.selected) {
          deferred = true;
          continue;
        }
        await _servers.removeServer(serverId: cur.id);
        changed = true;
      }
      index.remove(id);
    }

    // nothing selected (first start, or the selected server was removed): pick the first default server
    all = await _servers.getAllServers();
    if (all.isNotEmpty && !all.any((s) => s.serverData.selected)) {
      String? pick;
      for (final c in configs) {
        final it = index[c.id];
        if (it != null && all.any((s) => s.id == it.server)) {
          pick = it.server;
          break;
        }
      }
      await _servers.setSelectedServerId(id: pick ?? all.first.id);
      changed = true;
    }

    await prefs.setString(_indexKey, jsonEncode({for (final e in index.entries) e.key: e.value.toJson()}));
    _managed
      ..clear()
      ..addAll(index.values.map((e) => e.server));
    _deferred = deferred;
    return changed;
  }

  // ------------------------------------------------------------------ self-update

  Future<void> _update(Map<String, dynamic> feed, String source) async {
    if (build <= 0 || !Platform.isAndroid || _updating || _disposed) return;
    final apps = feed['apps'];
    final a = apps is Map<String, dynamic> ? apps['android'] : null;
    if (a is! Map<String, dynamic>) return;
    final rawCode = a['code'];
    final code = rawCode is num ? rawCode.toInt() : 0;
    if (code <= build) return;
    _updating = true;
    try {
      if (_stagedCode != code || _stagedPath == null) {
        final files = a['files'];
        if (files is! Map<String, dynamic>) return;
        final abis = await _channel.invokeListMethod<String>('abis') ?? const <String>[];
        Map<String, dynamic>? f;
        for (final abi in abis) {
          final x = files[abi];
          if (x is Map<String, dynamic>) {
            f = x;
            break;
          }
        }
        if (f == null) return;
        final url = f['url'];
        final sha = f['sha256'];
        final rawSize = f['size'];
        if (url is! String || sha is! String) return;
        final size = rawSize is num ? rawSize.toInt() : 0;
        final prefs = await SharedPreferences.getInstance();
        final saved = prefs.getStringList(_feedsKey) ?? const <String>[];
        final bases = <String>{source, ...saved, ...builtInFeeds}.where(_goodUrl).take(3);
        final urls = <String>[for (final b in bases) Uri.parse(b).resolve(url).toString()];
        final path = await _channel.invokeMethod<String>('download', <String, Object>{
          'urls': urls,
          'sha256': sha,
          'size': size,
          'name': 'SabiTunnel-$code.apk',
          'ua': 'SabiTunnel-Android/$build',
        });
        if (path == null) return;
        _stagedCode = code;
        _stagedPath = path;
      }
      await _maybeInstall();
    } catch (e) {
      debugPrint('[sabi] update: $e');
    } finally {
      _updating = false;
    }
  }

  /// On screen: Android asks the user (at most every 6 hours per build). In the background and disconnected:
  /// a silent update (Android 12+, once "install unknown apps" is allowed); otherwise it waits for the screen.
  Future<void> _maybeInstall() async {
    final path = _stagedPath;
    if (path == null) return;
    final quiet = !_foreground;
    if (quiet && await _vpnBusy()) return;
    final mode = quiet ? 'quiet' : 'ask';
    final prefs = await SharedPreferences.getInstance();
    final last = (prefs.getString(_offerKey) ?? '').split('|');
    final now = DateTime.now().millisecondsSinceEpoch;
    if (last.length == 3 && last[0] == '$_stagedCode' && last[1] == mode) {
      final t = int.tryParse(last[2]) ?? 0;
      if (now - t < const Duration(hours: 6).inMilliseconds) return;
    }
    await prefs.setString(_offerKey, '$_stagedCode|$mode|$now');
    await _channel.invokeMethod<void>('install', <String, Object>{'path': path, 'quiet': quiet});
  }
}
