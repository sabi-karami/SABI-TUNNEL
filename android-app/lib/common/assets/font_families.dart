class FontFamilies {
  /// Primary UI family. Kept under the historic name so every style picks it up;
  /// this edition ships Vazirmatn (Persian + Latin) instead of Roboto.
  static const roboto = 'Vazirmatn';

  /// proxy for the Apple system fonts (currently redirect to the equivalent of SF Pro Text and SF Pro Display)
  static const cupertinoSystemText = 'CupertinoSystemText';

  const FontFamilies._();
}
