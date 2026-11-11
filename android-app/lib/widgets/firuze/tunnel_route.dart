import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:trusttunnel/widgets/firuze/firuze_palette.dart';

/// Animated arc between the device and the server. Dashed while idle, a
/// glowing aurora beam with travelling light packets while connected.
class TunnelRoute extends StatefulWidget {
  final bool active;
  final bool busy;

  const TunnelRoute({
    super.key,
    required this.active,
    required this.busy,
  });

  @override
  State<TunnelRoute> createState() => _TunnelRouteState();
}

class _TunnelRouteState extends State<TunnelRoute> with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 1800),
  )..repeat();

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => SizedBox(
    height: 46,
    child: CustomPaint(
      painter: _RoutePainter(
        animation: _controller,
        active: widget.active,
        busy: widget.busy,
        direction: Directionality.of(context),
      ),
    ),
  );
}

class _RoutePainter extends CustomPainter {
  final Animation<double> animation;
  final bool active;
  final bool busy;
  final TextDirection direction;

  _RoutePainter({
    required this.animation,
    required this.active,
    required this.busy,
    required this.direction,
  }) : super(repaint: animation);

  @override
  void paint(Canvas canvas, Size size) {
    if (size.isEmpty) {
      return;
    }

    final rtl = direction == TextDirection.rtl;
    final start = Offset(rtl ? size.width - 6 : 6, size.height * 0.78);
    final end = Offset(rtl ? 6 : size.width - 6, size.height * 0.78);
    final path = Path()
      ..moveTo(start.dx, start.dy)
      ..quadraticBezierTo(size.width / 2, -size.height * 0.4, end.dx, end.dy);
    final metric = path.computeMetrics().first;

    if (!active && !busy) {
      final dash = Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 1.4
        ..strokeCap = StrokeCap.round
        ..color = Firuze.muted.withValues(alpha: 0.55);
      for (double d = 0; d < metric.length; d += 9) {
        canvas.drawPath(metric.extractPath(d, math.min(d + 4, metric.length)), dash);
      }

      return;
    }

    final color = active ? Firuze.turquoise : Firuze.gold;
    final bounds = Offset.zero & size;
    canvas.drawPath(
      path,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 6
        ..color = color.withValues(alpha: 0.2)
        ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 6),
    );
    canvas.drawPath(
      path,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 1.8
        ..strokeCap = StrokeCap.round
        ..shader = LinearGradient(
          colors: active
              ? const [Firuze.turquoise, Firuze.violet, Firuze.gold]
              : [Firuze.gold.withValues(alpha: 0.3), Firuze.gold, Firuze.gold.withValues(alpha: 0.3)],
        ).createShader(bounds),
    );

    final count = active ? 3 : 1;
    for (var i = 0; i < count; i++) {
      final p = (animation.value + i / count) % 1.0;
      final tangent = metric.getTangentForOffset(metric.length * p);
      if (tangent == null) {
        continue;
      }
      canvas.drawCircle(
        tangent.position,
        6,
        Paint()
          ..color = color.withValues(alpha: 0.4)
          ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 4),
      );
      canvas.drawCircle(tangent.position, 2.6, Paint()..color = Colors.white);
    }
  }

  @override
  bool shouldRepaint(covariant _RoutePainter oldDelegate) =>
      oldDelegate.active != active || oldDelegate.busy != busy || oldDelegate.direction != direction;
}
