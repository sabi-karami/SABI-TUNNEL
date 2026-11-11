import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:trusttunnel/widgets/firuze/firuze_palette.dart';

/// Frosted glass surface with a hairline border and an optional neon glow.
class GlassCard extends StatelessWidget {
  final Widget child;
  final EdgeInsetsGeometry padding;
  final EdgeInsetsGeometry margin;
  final double radius;
  final Color? glowColor;
  final bool blur;

  const GlassCard({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(16),
    this.margin = EdgeInsets.zero,
    this.radius = 22,
    this.glowColor,
    this.blur = true,
  });

  @override
  Widget build(BuildContext context) {
    final borderRadius = BorderRadius.circular(radius);
    final glow = glowColor;

    Widget content = DecoratedBox(
      decoration: BoxDecoration(
        borderRadius: borderRadius,
        gradient: const LinearGradient(
          begin: AlignmentDirectional.topStart,
          end: AlignmentDirectional.bottomEnd,
          colors: [Firuze.glassTop, Firuze.glassBottom],
        ),
        border: Border.all(
          color: glow == null ? Firuze.glassBorder : glow.withValues(alpha: 0.45),
        ),
      ),
      child: Material(
        type: MaterialType.transparency,
        child: Padding(
          padding: padding,
          child: child,
        ),
      ),
    );

    if (blur) {
      content = BackdropFilter(
        filter: ui.ImageFilter.blur(sigmaX: 16, sigmaY: 16),
        child: content,
      );
    }

    return Padding(
      padding: margin,
      child: DecoratedBox(
        decoration: BoxDecoration(
          borderRadius: borderRadius,
          boxShadow: [
            if (glow != null)
              BoxShadow(
                color: glow.withValues(alpha: 0.22),
                blurRadius: 28,
                spreadRadius: -6,
              ),
            const BoxShadow(
              color: Color(0x55000000),
              blurRadius: 24,
              offset: Offset(0, 10),
              spreadRadius: -12,
            ),
          ],
        ),
        child: ClipRRect(
          borderRadius: borderRadius,
          child: content,
        ),
      ),
    );
  }
}
