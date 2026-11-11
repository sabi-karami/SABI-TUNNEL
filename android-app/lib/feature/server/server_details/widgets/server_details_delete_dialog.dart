import 'package:flutter/material.dart';
import 'package:trusttunnel/common/extensions/context_extensions.dart';
import 'package:trusttunnel/common/extensions/theme_extensions.dart';
import 'package:trusttunnel/common/localization/localization.dart';
import 'package:trusttunnel/widgets/arb_parser/arb_parser.dart';
import 'package:trusttunnel/widgets/custom_alert_dialog.dart';

class ServerDetailsDeleteDialog extends StatelessWidget {
  final String serverName;
  final VoidCallback onDeletePressed;

  /// One of the panel's default servers: deleting it lasts only until the next sync.
  final bool isDefault;

  const ServerDetailsDeleteDialog({
    super.key,
    required this.serverName,
    required this.onDeletePressed,
    this.isDefault = false,
  });

  @override
  Widget build(BuildContext context) => CustomAlertDialog(
    title: context.ln.deleteServerDialogTitle,
    scrollable: true,
    content: isDefault
        ? Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              ArbParser(
                data: context.ln.deleteServerDescription(
                  serverName,
                ),
              ),
              const SizedBox(height: 12),
              Text(
                context.ln.deleteDefaultServerNote,
                style: context.textTheme.bodySmall,
              ),
            ],
          )
        : ArbParser(
            data: context.ln.deleteServerDescription(
              serverName,
            ),
          ),
    actionsBuilder: (spacing) => [
      TextButton(
        onPressed: context.pop,
        child: Text(
          context.ln.cancel,
        ),
      ),
      Theme(
        data: context.theme.copyWith(
          textButtonTheme: context.theme.extension<CustomTextButtonTheme>()?.danger,
        ),
        child: TextButton(
          onPressed: () {
            context.pop();
            onDeletePressed();
          },
          child: Text(
            context.ln.delete,
          ),
        ),
      ),
    ],
  );
}
