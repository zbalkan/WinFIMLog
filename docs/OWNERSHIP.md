# Configuration ownership

The Windows/GPO team owns `HKLM\SOFTWARE\Policies\WinFIMLog`; the host operations team owns fallback preferences at `HKLM\SOFTWARE\WinFIMLog`. Security monitoring approves scope changes. Policy has value-by-value precedence over local baselines and preferences.

Changes require a ticket containing the proposed effective scope and reviewer. Event 7794 notifies the SIEM with previous/new `ScopeHash`; administrators confirm the following heartbeat carries the new hash. Configuration drift is corrected through GPO reapplication, not by editing policy locally. Removing a policy deliberately restores its preference/default fallback. Backup and restoration must preserve preferences, while policy is restored from the directory/GPO authority.
