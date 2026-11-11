import 'package:flutter/material.dart';

/// «شب فیروزه» (Turquoise Night) design tokens used by the decorative layer
/// of this edition: the night sky, glass cards and the tunnel portal.
abstract final class Firuze {
  static const night = Color(0xFF060814);
  static const nightTop = Color(0xFF0B1030);
  static const nightDeep = Color(0xFF04050D);
  static const ink = Color(0xFF0D1236);

  static const turquoise = Color(0xFF2DE2E6);
  static const turquoiseSoft = Color(0xFF7FF3F5);
  static const turquoiseDeep = Color(0xFF0B6E7A);
  static const violet = Color(0xFF8B5CF6);
  static const magenta = Color(0xFFFF4FD8);
  static const gold = Color(0xFFF5C76B);
  static const rose = Color(0xFFFF5C7A);

  static const text = Color(0xFFEFF2FF);
  static const muted = Color(0xFF8E97C6);
  static const onAccent = Color(0xFF041218);

  static const glassTop = Color(0x1FFFFFFF);
  static const glassBottom = Color(0x08FFFFFF);
  static const glassBorder = Color(0x26FFFFFF);

  static const auroraGradient = LinearGradient(
    colors: [turquoise, violet, magenta],
  );

  static const _faDigits = '۰۱۲۳۴۵۶۷۸۹';

  /// Converts Latin digits to Persian digits for display.
  static String faDigits(String value) => value.replaceAllMapped(
    RegExp('[0-9]'),
    (match) => _faDigits[int.parse(match.group(0)!)],
  );
}
