import 'package:flutter/material.dart';
import 'package:trusttunnel/common/assets/asset_icons.dart';

class CustomIcon extends StatelessWidget {
  final IconData icon;
  final Color? color;
  final double? size;

  const CustomIcon({
    super.key,
    required this.icon,
    this.size,
    this.color,
  });

  factory CustomIcon.small({
    required IconData icon,
    Color? color,
  }) => CustomIcon(
    icon: icon,
    color: color,
    size: 18,
  );

  factory CustomIcon.medium({
    required IconData icon,
    Color? color,
  }) => CustomIcon(
    icon: icon,
    color: color,
    size: 24,
  );

  /// Directional glyphs of the icon font that must be mirrored in RTL (Persian) layouts.
  static final Set<int> _directionalCodePoints = {
    AssetIcons.arrowBack.codePoint,
    AssetIcons.arrowForward.codePoint,
    AssetIcons.arrowLeft.codePoint,
    AssetIcons.arrowRight.codePoint,
    AssetIcons.navigateBefore.codePoint,
    AssetIcons.navigateNext.codePoint,
  };

  @override
  Widget build(BuildContext context) {
    final iconWidget = Icon(
      icon,
      color: color,
      size: size,
      applyTextScaling: true,
    );

    final mirror = icon.fontFamily == AssetIcons.arrowBack.fontFamily &&
        _directionalCodePoints.contains(icon.codePoint) &&
        Directionality.maybeOf(context) == TextDirection.rtl;

    return mirror ? Transform.flip(flipX: true, child: iconWidget) : iconWidget;
  }
}
