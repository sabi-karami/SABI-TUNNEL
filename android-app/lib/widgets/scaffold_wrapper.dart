import 'package:flutter/material.dart';
import 'package:trusttunnel/common/extensions/context_extensions.dart';
import 'package:trusttunnel/widgets/firuze/night_sky.dart';

/// Paints the animated «شب فیروزه» night sky behind every top-level screen.
/// Scaffolds are transparent in this edition, so the sky shows through.
class ScaffoldWrapper extends StatelessWidget {
  final Widget child;

  const ScaffoldWrapper({
    super.key,
    required this.child,
  });

  @override
  Widget build(BuildContext context) => Stack(
    fit: StackFit.expand,
    children: [
      const NightSky(),
      Padding(
        padding: EdgeInsets.all(context.isMobileBreakpoint ? 0 : 16),
        child: ClipRRect(
          borderRadius: BorderRadius.circular(context.isMobileBreakpoint ? 0 : 16),
          child: child,
        ),
      ),
    ],
  );
}
