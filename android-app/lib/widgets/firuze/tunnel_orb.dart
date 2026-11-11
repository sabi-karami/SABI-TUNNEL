import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:trusttunnel/common/assets/asset_icons.dart';
import 'package:trusttunnel/data/model/vpn_state.dart';
import 'package:trusttunnel/widgets/firuze/firuze_palette.dart';

enum OrbMood { idle, busy, live, offline }

/// The connect button of this edition: a Persian pointed-arch portal inside a
/// golden khatam star, ringed by a living aurora halo.
class TunnelOrb extends StatefulWidget {
  final VpnState state;
  final bool enabled;
  final VoidCallback? onTap;
  final double size;
  final String semanticLabel;

  const TunnelOrb({
    super.key,
    required this.state,
    required this.onTap,
    required this.semanticLabel,
    this.enabled = true,
    this.size = 236,
  });

  @override
  State<TunnelOrb> createState() => _TunnelOrbState();
}

class _TunnelOrbState extends State<TunnelOrb> with TickerProviderStateMixin {
  late final AnimationController _spin = AnimationController(
    vsync: this,
    duration: const Duration(seconds: 9),
  )..repeat();

  late final AnimationController _pulse = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 2600),
  )..repeat();

  bool _pressed = false;

  @override
  void dispose() {
    _spin.dispose();
    _pulse.dispose();
    super.dispose();
  }

  OrbMood get _mood {
    if (!widget.enabled) {
      return OrbMood.offline;
    }

    return switch (widget.state) {
      VpnState.connected => OrbMood.live,
      VpnState.disconnected => OrbMood.idle,
      _ => OrbMood.busy,
    };
  }

  void _setPressed(bool value) {
    if (_pressed != value) {
      setState(() => _pressed = value);
    }
  }

  @override
  Widget build(BuildContext context) {
    final mood = _mood;
    final onTap = widget.onTap;
    final iconColor = switch (mood) {
      OrbMood.live || OrbMood.busy => Firuze.onAccent,
      OrbMood.idle => Firuze.text,
      OrbMood.offline => Firuze.text.withValues(alpha: 0.5),
    };

    return Semantics(
      button: true,
      label: widget.semanticLabel,
      child: GestureDetector(
        behavior: HitTestBehavior.opaque,
        onTapDown: onTap == null ? null : (_) => _setPressed(true),
        onTapCancel: () => _setPressed(false),
        onTapUp: (_) => _setPressed(false),
        onTap: onTap == null
            ? null
            : () {
                HapticFeedback.mediumImpact();
                onTap();
              },
        child: AnimatedScale(
          scale: _pressed ? 0.94 : 1,
          duration: const Duration(milliseconds: 160),
          curve: Curves.easeOut,
          child: SizedBox.square(
            dimension: widget.size,
            child: CustomPaint(
              painter: _OrbPainter(spin: _spin, pulse: _pulse, mood: mood),
              child: Align(
                alignment: const Alignment(0, 0.145),
                child: AnimatedSwitcher(
                  duration: const Duration(milliseconds: 300),
                  child: Icon(
                    AssetIcons.powerSettingsNew,
                    key: ValueKey(mood),
                    size: widget.size * 0.105,
                    color: iconColor,
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _OrbPainter extends CustomPainter {
  final Animation<double> spin;
  final Animation<double> pulse;
  final OrbMood mood;

  _OrbPainter({
    required this.spin,
    required this.pulse,
    required this.mood,
  }) : super(repaint: Listenable.merge([spin, pulse]));

  Color get _tone => switch (mood) {
    OrbMood.live => Firuze.turquoise,
    OrbMood.busy => Firuze.gold,
    OrbMood.idle => Firuze.violet,
    OrbMood.offline => Firuze.muted,
  };

  @override
  void paint(Canvas canvas, Size size) {
    final s = size.shortestSide;
    final c = size.center(Offset.zero);
    final tone = _tone;
    final spinAngle = spin.value * math.pi * 2;

    // Halo.
    _radial(
      canvas,
      c,
      s * 0.5,
      [tone.withValues(alpha: mood == OrbMood.live ? 0.42 : 0.22), tone.withValues(alpha: 0)],
    );

    // Pulse rings.
    if (mood == OrbMood.live || mood == OrbMood.busy) {
      for (var i = 0; i < 3; i++) {
        final p = (pulse.value + i / 3) % 1.0;
        canvas.drawCircle(
          c,
          s * (0.33 + 0.17 * p),
          Paint()
            ..style = PaintingStyle.stroke
            ..strokeWidth = 0.5 + 2 * (1 - p)
            ..color = tone.withValues(alpha: 0.55 * (1 - p)),
        );
      }
    }

    // Golden khatam star, slowly counter-rotating.
    canvas.save();
    canvas.translate(c.dx, c.dy);
    canvas.rotate(-spinAngle / 4);
    final star = Path();
    for (var sq = 0; sq < 2; sq++) {
      final rot = sq * math.pi / 4 + math.pi / 4;
      for (var i = 0; i < 4; i++) {
        final a = rot + i * math.pi / 2;
        final p = Offset(math.cos(a), math.sin(a)) * (s * 0.46);
        if (i == 0) {
          star.moveTo(p.dx, p.dy);
        } else {
          star.lineTo(p.dx, p.dy);
        }
      }
      star.close();
    }
    canvas.drawPath(
      star,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 1.6
        ..strokeJoin = StrokeJoin.round
        ..color = Firuze.gold.withValues(alpha: mood == OrbMood.offline ? 0.25 : 0.7),
    );
    canvas.restore();

    // Aurora ring.
    final ringRect = Rect.fromCircle(center: c, radius: s * 0.395);
    final ringPaint = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = s * 0.022
      ..strokeCap = StrokeCap.round;
    switch (mood) {
      case OrbMood.live:
        canvas.drawArc(
          ringRect,
          0,
          math.pi * 2,
          false,
          Paint()
            ..style = PaintingStyle.stroke
            ..strokeWidth = s * 0.05
            ..color = tone.withValues(alpha: 0.35)
            ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 12),
        );
        ringPaint.shader = SweepGradient(
          colors: const [Firuze.turquoise, Firuze.violet, Firuze.magenta, Firuze.gold, Firuze.turquoise],
          transform: GradientRotation(spinAngle),
        ).createShader(ringRect);
        canvas.drawArc(ringRect, 0, math.pi * 2, false, ringPaint);
      case OrbMood.busy:
        canvas.drawArc(
          ringRect,
          0,
          math.pi * 2,
          false,
          Paint()
            ..style = PaintingStyle.stroke
            ..strokeWidth = s * 0.012
            ..color = Firuze.gold.withValues(alpha: 0.18),
        );
        ringPaint.shader = SweepGradient(
          colors: [Firuze.gold.withValues(alpha: 0), Firuze.gold, Firuze.turquoiseSoft, Firuze.turquoiseSoft],
          stops: const [0, 0.5, 0.6, 1],
          transform: GradientRotation(spinAngle * 3),
        ).createShader(ringRect);
        canvas.drawArc(ringRect, spinAngle * 3, math.pi * 1.2, false, ringPaint);
      case OrbMood.idle || OrbMood.offline:
        ringPaint.color = tone.withValues(alpha: mood == OrbMood.idle ? 0.55 : 0.25);
        canvas.drawArc(ringRect, 0, math.pi * 2, false, ringPaint);
    }

    // Core.
    final rc = s * 0.33;
    final coreRect = Rect.fromCircle(center: c, radius: rc);
    final coreColors = switch (mood) {
      OrbMood.live => const [Color(0xFF0E5C6B), Color(0xFF0A2C4A), Firuze.ink],
      OrbMood.busy => const [Color(0xFF4A3B1C), Color(0xFF1E1B33), Firuze.ink],
      OrbMood.idle => const [Color(0xFF2A2F6A), Color(0xFF151A45), Firuze.ink],
      OrbMood.offline => const [Color(0xFF1C2148), Color(0xFF12163A), Firuze.ink],
    };
    canvas.drawCircle(
      c,
      rc,
      Paint()
        ..shader = RadialGradient(
          center: const Alignment(0, 0.35),
          radius: 0.9,
          colors: coreColors,
          stops: const [0, 0.6, 1],
        ).createShader(coreRect),
    );
    canvas.drawCircle(
      c,
      rc,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 1
        ..color = Colors.white.withValues(alpha: 0.12),
    );

    // Portal (pointed arch) with receding arches and the light at its end.
    canvas.save();
    canvas.clipPath(Path()..addOval(coreRect));
    final vp = c + Offset(0, rc * 0.22);
    final lightColor = switch (mood) {
      OrbMood.live => Firuze.turquoiseSoft,
      OrbMood.busy => Firuze.gold,
      OrbMood.idle => Firuze.violet,
      OrbMood.offline => Firuze.muted,
    };
    final intensity = switch (mood) {
      OrbMood.live => 1.0,
      OrbMood.busy => 0.6 + 0.4 * math.sin(pulse.value * math.pi * 2).abs(),
      OrbMood.idle => 0.4,
      OrbMood.offline => 0.2,
    };
    final lightRadius = rc * 0.62;
    canvas.drawCircle(
      vp,
      lightRadius,
      Paint()
        ..shader = RadialGradient(
          colors: [
            Colors.white.withValues(alpha: 0.95 * intensity),
            lightColor.withValues(alpha: 0.75 * intensity),
            lightColor.withValues(alpha: 0),
          ],
          stops: const [0, 0.25, 1],
        ).createShader(Rect.fromCircle(center: vp, radius: lightRadius)),
    );

    final arch = _archPoints(c, rc);
    final archColor = switch (mood) {
      OrbMood.live => Firuze.turquoise,
      OrbMood.busy => Firuze.gold,
      OrbMood.idle => Firuze.turquoise.withValues(alpha: 0.75),
      OrbMood.offline => Firuze.muted.withValues(alpha: 0.5),
    };
    const factors = [0.74, 0.52, 0.34];
    for (var i = 0; i < factors.length; i++) {
      final f = factors[i];
      final scaled = [for (final p in arch) vp + (p - vp) * f];
      canvas.drawPath(
        Path()..addPolygon(scaled, false),
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 1.6
          ..strokeCap = StrokeCap.round
          ..color = archColor.withValues(alpha: archColor.a * (0.7 - i * 0.16)),
      );
    }
    final archPath = Path()..addPolygon(arch, false);
    canvas.drawPath(
      archPath,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = s * 0.03
        ..strokeCap = StrokeCap.round
        ..color = archColor.withValues(alpha: archColor.a * 0.35)
        ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 6),
    );
    canvas.drawPath(
      archPath,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = s * 0.014
        ..strokeCap = StrokeCap.round
        ..strokeJoin = StrokeJoin.round
        ..color = archColor,
    );
    canvas.restore();
  }

  static List<Offset> _archPoints(Offset c, double rc) {
    final a = rc * 0.42;
    final ys = c.dy - rc * 0.05;
    final yb = c.dy + rc * 0.55;
    final r = a * 1.3;
    final ccx = c.dx + a - r;
    final th = math.acos((c.dx - ccx) / r);
    const n = 40;
    final right = [
      for (var i = 0; i <= n; i++) Offset(ccx + r * math.cos(th * i / n), ys - r * math.sin(th * i / n)),
    ];
    final left = [
      for (final p in right.reversed.skip(1)) Offset(2 * c.dx - p.dx, p.dy),
    ];

    return [Offset(c.dx + a, yb), ...right, ...left, Offset(c.dx - a, yb)];
  }

  static void _radial(Canvas canvas, Offset center, double radius, List<Color> colors) {
    canvas.drawCircle(
      center,
      radius,
      Paint()
        ..shader = RadialGradient(colors: colors).createShader(
          Rect.fromCircle(center: center, radius: radius),
        ),
    );
  }

  @override
  bool shouldRepaint(covariant _OrbPainter oldDelegate) => oldDelegate.mood != mood;
}
