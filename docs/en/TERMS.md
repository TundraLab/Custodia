# Terms of Use — Custódia

> 🌐 **Language / Idioma:** [🇧🇷 Português](../../TERMS.md) | **🇺🇸 English (current)**

**Last updated:** November 24, 2026  
**Owner / Developer:** TundraLab (`contato.tundralab@gmail.com`)  
**Official repository:** https://github.com/TundraLab/Custodia

By downloading, installing, compiling, or using **Custódia**, you state that you have read and agree to these Terms of Use, the [Privacy Policy](PRIVACY.md), and the [Proprietary License Agreement](../../LICENSE).

---

## 1. Subject Matter and Licensing

**Custódia** is a desktop client software for local Google Drive synchronization and management on Linux systems, exclusively owned by **TundraLab** (`Copyright (c) 2026 TundraLab. All Rights Reserved.`).

- Specific terms regarding source code access, prohibition of unauthorized commercial redistribution, trademark usage, and intellectual property are governed by the [LICENSE](../../LICENSE) file.
- Nothing in these Terms grants any rights to trademarks, logos, or intellectual property of TundraLab or Google LLC.

---

## 2. Integration with Google Services and User Credentials

To use Google Drive synchronization, you must authenticate your own Google Account:

1. **BYO-OAuth Model (Personal/Individual Use):** in the default setup described in the repository, each user creates and uses their own OAuth 2.0 credentials in Google Cloud Console (sensitive scope `https://www.googleapis.com/auth/drive`). You are solely responsible for safeguarding, configuring, and ensuring compliance of your Google Cloud Console project, as well as your account's quota limits.
2. **Prohibition on Distributing Third-Party Credentials:** you must not reuse third-party OAuth credentials without authorization or redistribute modified versions of the application with unverified Client IDs to third parties as if they were official.
3. **Google Compliance:** when connecting the application to Google Drive, you also remain subject to Google's Terms of Service and the [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy).

---

## 3. Responsibility for Data and Synchronization

Custódia performs local and remote upload, update, deletion, and mirroring operations according to the synchronization rules you configure:

- **Backups:** before enabling two-way synchronization or batch operations on critical directories, maintaining independent backups of your important files is recommended.
- **Local security:** physical and logical protection of your Linux machine (system user, disk encryption, folder permissions) is solely the user's responsibility.

---

## 4. Optional Account Communications

If you enable account lifecycle alerts in the application, TundraLab will send only transactional connection or revocation confirmation emails from `contato.tundralab@gmail.com`, using solely your email address and profile name as detailed in the [Privacy Policy](PRIVACY.md). This feature is optional and can be disabled at any time in settings.

---

## 5. Disclaimer of Warranties and Limitation of Liability

To the maximum extent permitted by applicable law:

- The software is provided **"AS IS"**, without express or implied warranties of continuous availability, fitness for a particular purpose, freedom from interruptions caused by Google API changes, or automatic recovery of externally deleted data.
- TundraLab is not liable for direct, indirect, or incidental damages, file loss, Google Drive unavailability, or Google Cloud project suspension resulting from misuse by the user.
- Custódia is independent software developed by TundraLab and **is not affiliated with, sponsored by, or endorsed by Google LLC**. "Google Drive" and "Google Cloud" are registered trademarks of Google LLC.

---

## 6. Changes and Contact

These Terms may be updated to reflect technical, legal, or Google API policy changes, displaying the new version in the application when applicable.

Legal inquiries, commercial requests, or questions about these Terms may be sent to **`contato.tundralab@gmail.com`**.
