import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter/scheduler.dart';
import 'package:trusttunnel/widgets/firuze/firuze_palette.dart';

/// Animated «شب فیروزه» backdrop: night gradient, drifting aurora, twinkling
/// stars, a faint girih (khatam) lattice and an occasional shooting star.
class NightSky extends StatefulWidget {
  const NightSky({super.key});

  @override
  State<NightSky> createState() => _NightSkyState();
}

class _NightSkyState extends State<NightSky> with SingleTickerProviderStateMixin {
  static const _cycle = Duration(seconds: 28);

  /// The sky moves slowly, so ~20 fps looks identical to 60 fps while costing
  /// a third of the work (and of the glass-card re-blurs on top of it).
  static const _frameInterval = Duration(milliseconds: 50);

  final ValueNotifier<double> _phase = ValueNotifier<double>(0.3);
  late final Ticker _ticker = createTicker(_onTick);
  Duration _lastFrame = Duration.zero;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final reduceMotion = MediaQuery.maybeDisableAnimationsOf(context) ?? false;
    if (reduceMotion) {
      if (_ticker.isActive) {
        _ticker.stop();
      }
    } else if (!_ticker.isActive) {
      _lastFrame = Duration.zero;
      _ticker.start();
    }
  }

  void _onTick(Duration elapsed) {
    if (elapsed - _lastFrame < _frameInterval && _lastFrame != Duration.zero) {
      return;
    }
    _lastFrame = elapsed;
    _phase.value = (elapsed.inMilliseconds % _cycle.inMilliseconds) / _cycle.inMilliseconds;
  }

  @override
  void dispose() {
    _ticker.dispose();
    _phase.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Stack(
    fit: StackFit.expand,
    children: [
      // Static layer: painted once and cached by its RepaintBoundary.
      const RepaintBoundary(
        child: CustomPaint(
          painter: _StaticSkyPainter(),
          isComplex: true,
          child: SizedBox.expand(),
        ),
      ),
      RepaintBoundary(
        child: CustomPaint(
          painter: _NightSkyPainter(_phase),
          willChange: true,
          child: const SizedBox.expand(),
        ),
      ),
    ],
  );
}

class _StaticSkyPainter extends CustomPainter {
  const _StaticSkyPainter();

  @override
  void paint(Canvas canvas, Size size) {
    if (size.isEmpty) {
      return;
    }
    final rect = Offset.zero & size;
    canvas.drawRect(
      rect,
      Paint()
        ..shader = const LinearGradient(
          begin: Alignment.topCenter,
          end: Alignment.bottomCenter,
          colors: [Firuze.nightTop, Firuze.night, Firuze.nightDeep],
          stops: [0, 0.55, 1],
        ).createShader(rect),
    );
    _NightSkyPainter._paintGirih(canvas, size);
  }

  @override
  bool shouldRepaint(covariant _StaticSkyPainter oldDelegate) => false;
}

class _Star {
  final double x;
  final double y;
  final double radius;
  final double phase;
  final int speed;

  const _Star(this.x, this.y, this.radius, this.phase, this.speed);
}

class _NightSkyPainter extends CustomPainter {
  final ValueListenable<double> animation;

  _NightSkyPainter(this.animation) : super(repaint: animation);

  static final List<_Star> _stars = _generateStars();

  static List<_Star> _generateStars() {
    final random = math.Random(1404);

    return List.generate(
      120,
      (_) => _Star(
        random.nextDouble(),
        random.nextDouble(),
        0.4 + random.nextDouble() * 1.3,
        random.nextDouble() * math.pi * 2,
        1 + random.nextInt(3),
      ),
    );
  }

  @override
  void paint(Canvas canvas, Size size) {
    if (size.isEmpty) {
      return;
    }

    final rect = Offset.zero & size;
    final w = size.width;
    final h = size.height;
    final v = animation.value * math.pi * 2;

    _glow(
      canvas,
      Offset(w * (0.18 + 0.10 * math.sin(v)), h * (0.10 + 0.04 * math.cos(v))),
      w * 0.85,
      Firuze.turquoise.withValues(alpha: 0.20),
    );
    _glow(
      canvas,
      Offset(w * (0.88 + 0.08 * math.cos(v)), h * (0.26 + 0.05 * math.sin(v))),
      w * 0.80,
      Firuze.violet.withValues(alpha: 0.22),
    );
    _glow(
      canvas,
      Offset(w * (0.50 + 0.22 * math.sin(v + 1.7)), h * 1.02),
      w * 0.90,
      Firuze.magenta.withValues(alpha: 0.10),
    );

    final ribbon = Path();
    const segments = 48;
    for (var i = 0; i <= segments; i++) {
      final p = i / segments;
      final x = w * p;
      final y =
          h * 0.16 + math.sin(p * math.pi * 2 + v) * h * 0.035 + math.sin(p * math.pi * 5 - v * 2) * h * 0.012;
      if (i == 0) {
        ribbon.moveTo(x, y);
      } else {
        ribbon.lineTo(x, y);
      }
    }
    canvas.drawPath(
      ribbon,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = h * 0.07
        ..strokeCap = StrokeCap.round
        ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 32)
        ..shader = LinearGradient(
          colors: [
            Firuze.turquoise.withValues(alpha: 0),
            Firuze.turquoise.withValues(alpha: 0.30),
            Firuze.violet.withValues(alpha: 0.26),
            Firuze.magenta.withValues(alpha: 0),
          ],
        ).createShader(rect),
    );

    final starPaint = Paint();
    for (final star in _stars) {
      final twinkle = 0.5 + 0.5 * math.sin(v * star.speed + star.phase);
      starPaint.color = Colors.white.withValues(alpha: 0.15 + 0.6 * twinkle * (1 - star.y * 0.6));
      canvas.drawCircle(Offset(star.x * w, star.y * h), star.radius, starPaint);
    }

    final t = animation.value;
    if (t > 0.62 && t < 0.70) {
      final p = (t - 0.62) / 0.08;
      final head = Offset(w * (0.85 - 0.5 * p), h * (0.06 + 0.16 * p));
      final tail = head + Offset(w * 0.16, -h * 0.05);
      canvas.drawLine(
        head,
        tail,
        Paint()
          ..strokeWidth = 1.6
          ..strokeCap = StrokeCap.round
          ..shader = LinearGradient(
            colors: [
              Colors.white.withValues(alpha: 0.9 * (1 - p)),
              Colors.white.withValues(alpha: 0),
            ],
          ).createShader(Rect.fromPoints(head, tail)),
      );
    }
  }

  static void _paintGirih(Canvas canvas, Size size) {
    const step = 72.0;
    final paint = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 0.7;
    final cols = (size.width / step).ceil() + 1;
    final rows = (size.height / step).ceil() + 1;
    for (var row = 0; row < rows; row++) {
      final fade = 1 - (row * step / size.height).clamp(0.0, 1.0);
      paint.color = Firuze.gold.withValues(alpha: 0.012 + 0.05 * fade);
      for (var col = 0; col < cols; col++) {
        final center = Offset(col * step + (row.isOdd ? step / 2 : 0), row * step);
        canvas.drawPath(_khatam(center, step * 0.36), paint);
      }
    }
  }

  static Path _khatam(Offset c, double r) {
    final path = Path();
    for (var s = 0; s < 2; s++) {
      final rot = s * math.pi / 4 + math.pi / 4;
      for (var i = 0; i < 4; i++) {
        final a = rot + i * math.pi / 2;
        final p = c + Offset(math.cos(a) * r, math.sin(a) * r);
        if (i == 0) {
          path.moveTo(p.dx, p.dy);
        } else {
          path.lineTo(p.dx, p.dy);
        }
      }
      path.close();
    }

    return path;
  }

  static void _glow(Canvas canvas, Offset center, double radius, Color color) {
    canvas.drawCircle(
      center,
      radius,
      Paint()
        ..shader = RadialGradient(
          colors: [color, color.withValues(alpha: 0)],
        ).createShader(Rect.fromCircle(center: center, radius: radius)),
    );
  }

  @override
  bool shouldRepaint(covariant _NightSkyPainter oldDelegate) => oldDelegate.animation != animation;
}
