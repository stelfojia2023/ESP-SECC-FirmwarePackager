> FTP 现在既可默认上传刚生成的联合包（上传前校验），也可独立选择任意本地文件直接上传。
> **界面试用 V0.0.1**：暖灰白卡片、分组操作、动态文件列表、内嵌 FTP 操作区与默认启用音效。[改动与验证边界](docs/UI-V0.0.1.md)。工具版本与包格式 V1 独立。
> 本分支 `dist/FirmwarePackager.exe` 为 Mono 交叉编译 Windows x64 试用产物；Windows 原生构建与日志见 Actions 的 `FirmwarePackager-V0.0.1-windows` 产物。真实 Windows GUI/拖放/DPI/音效体验尚需实机确认。

# ESP-SECC 固件联合打包工具 V0.0.1

本工具将 PFC、LLC、M4、PLC、ESP32 中选定的 1～5 份固件打包成一个 BIN，可校验 V1 联合包，也可按需上传到 FTP。交付的 `dist/FirmwarePackager.exe` 是 Windows 10/11 64 位图形程序，不需要安装开发环境。运行依赖 Windows 自带的 .NET Framework 4.x；打包与校验无需联网，FTP 上传需要网络和服务器。

[单独下载 FirmwarePackager.exe](https://github.com/stelfojia2023/ESP-SECC-FirmwarePackager/raw/refs/heads/ui/warm-cards-v0.0.1/dist/FirmwarePackager.exe) · [下载五类测试 BIN](https://github.com/stelfojia2023/ESP-SECC-FirmwarePackager/raw/refs/heads/main/test-vectors/five-test-bins.zip)。仓库为私有仓库，需要登录有权限的 GitHub 账号。

**V1 是候选包格式。当前设备尚未接入联合包解析和多模块升级；候选的 6 MiB 暂存区和 2.5 MiB ESP32 应用槽不是当前设备配置。** 上传仅发送文件到用户指定的 FTP 目录，不烧录设备、不执行升级。

**一键加载目前仅为禁用界面，文件识别规则尚未实现；不代表支持自动识别文件夹。**

## 使用方法

1. 双击 `dist/FirmwarePackager.exe`。工具、输入固件和输出路径可包含中文与空格。
2. 将 BIN 文件拖入对应类型的输入框，也可以粘贴或输入文件路径，或点击文件夹图标。每类最多选择一个文件，未选择的类型不会占用固件空间。
3. 名称默认为原文件名，可修改；为每个已选固件手动填写版本，例如 `V000B01D15`。名称和版本长度按 UTF-8 字节数限制，超长会报错，不会截断。
4. 已选固件自动显示在下方列表，清除选择后对应行消失；输入完整通过校验后显示实际包内偏移，否则显示“待校验”。错误与预算提醒显示在操作结果。偏移相对于联合 BIN 起点，不是目标烧录地址。
5. 选择输出 BIN 路径，点击“包生成”。已有输出需要确认替换；输出不能与任何输入固件指向同一文件。成功后结果区显示总大小及每类固件的名称、版本和大小。
6. 点击“包校验”，选择本工具的 V1 BIN。通过时结果区显示包信息和目录；文件不受支持、损坏、截断、长度不符或校验失败时显示原因。
7. 在右侧文件/URL/用户名/密码区域操作。生成后默认填入刚生成的联合包；也可不打包，直接拖入或浏览选择任意本地文件。粘贴 `ftp://用户名:密码@服务器:端口/目录`，或分别填写 FTP 地址、用户名和密码；点击“上传到 FTP”并确认目标。目录后自动追加当前所选文件的文件名。用户名或密码中的 `@`、`:` 等 URL 特殊字符需百分号编码，密码仅在当前窗口中保留且遮罩显示。

联合包模式上传前重新校验 V1 格式；独立本地文件模式直接上传，不强加包格式或 6 MiB 限制。两种模式均收到 FTP 服务器最终成功应答后才显示成功；失败时显示原因。普通 FTP 以明文传输账号、密码和文件，服务器上同名文件可能被覆盖。成功表示服务器确认传输完成，不包含远端文件回读校验，也不代表设备已升级。

ESP32 固件超过 2.5 MiB，或整个包超过 6 MiB 时拒绝打包。PFC、LLC、M4、PLC 超过各自建议预算时会提醒，但满足总大小限制时仍可生成。数据读取期间输入文件发生变化、输出不可写、与输入相同或生成中途失败时不会报告成功；生成先写同目录临时文件、校验后再发布到目标路径。

底层整包 MD5 按包头 MD5 字段置零规则计算，**不同于直接对最终 BIN 执行普通 `md5sum`**。MD5 只检查数据一致性，不验证发布者身份。

## 交付内容

- `dist/FirmwarePackager.exe`：单文件图形程序。
- `src/`：打包、校验和 Windows Forms 界面源码。
- `assets/linchr-mascot.ico`：程序图标，已嵌入 EXE，运行时无需单独携带。
- `docs/使用说明.md`：面向操作人员的简明说明。
- `docs/V1格式说明.md`：候选格式的字节布局、校验和扩展规则。
- `test-vectors/`：可复核的 M4 + ESP32 样包、原始输入及预期结果，内容均为合成测试数据。
- `test-vectors/five-test-bins.zip`：PFC、LLC、M4、PLC、ESP32 五个合成输入文件，仅用于工具测试，不能用于设备升级。
- `tests/`：组合、异常、GUI 和独立格式核验脚本。
- `docs/验证记录.md`：第一版构建和测试记录；`docs/界面美化验证.md`：界面、拖拽及 FTP 改版的验证记录。`evidence/SHA256SUMS` 对应第一版，改版的校验清单位于 `evidence/ui-refresh/`。

## 在 Windows 上构建

准备 Windows 10/11 64 位和系统的 .NET Framework 4.x；构建使用系统 `Framework64/v4.0.30319/csc.exe`，不需下载包。将本工程复制到 Windows 磁盘，在工程目录打开 PowerShell：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

输出为 `dist\FirmwarePackager.exe`。该脚本仅使用 Windows 和 .NET Framework 自带程序集，没有第三方构建依赖。

## 复跑测试

先按上文构建，然后在 Windows 已解锁的交互桌面执行；GUI 测试会短暂打开窗口并操作文件对话框。硬链接保护测试要求临时目录所在卷支持硬链接（如 NTFS）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\run-tests.ps1
```

脚本打印临时证据目录，其中 `core results` 保存 31 组、预算上限测试包及输入。用 Python 3.10 或更高版本（仅独立核验脚本需要，工具本身不需要）核验该目录：

```powershell
python .\tests\independent_verify.py "<打印的证据目录>\core results"
python .\tests\verify_vector.py .\test-vectors
```

测试会创建自己的新临时目录，不覆盖已有测试证据。请不要把测试数据误当成正式固件。

