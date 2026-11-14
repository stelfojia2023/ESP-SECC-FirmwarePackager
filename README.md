# ESP-SECC 固件联合打包工具 V1

本工具离线将 PFC、LLC、M4、PLC、ESP32 中选定的 1～5 份固件打包成一个 BIN，也可打开并校验由本工具生成的 V1 联合包。交付的 `dist/FirmwarePackager.exe` 是 Windows 10/11 64 位图形程序，不需要安装开发环境。运行依赖 Windows 自带的 .NET Framework 4.x；不需要第三方库或网络服务。

**V1 是待评审格式。当前设备尚未接入联合包解析和多模块升级；候选的 6 MiB 暂存区和 2.5 MiB ESP32 应用槽不是当前设备配置。** 工具不发送 TCP、不下载 FTP、不烧录设备、不执行升级。

## 使用方法

1. 双击 `dist/FirmwarePackager.exe`。工具、输入固件和输出路径可包含中文与空格。
2. 在需要的固件类型行点击“浏览”，每类最多选择一个文件。未选择的类型不会占用固件空间。
3. 名称默认为原文件名，可修改；为每个已选固件手动填写版本，例如 `V000B01D15`。名称和版本长度按 UTF-8 字节数限制，超长会报错，不会截断。
4. 查看“打包前预览”的类型、名称、版本、原始大小和包内偏移，以及总大小、输入错误或预算提醒。偏移相对于联合 BIN 起点，不是目标烧录地址。
5. 选择输出 BIN 路径，点击“生成联合包”。已有输出需要确认替换；输出不能与任何输入固件指向同一文件。成功后结果区显示总大小、整包 MD5 和各固件的信息与 MD5。
6. 点击“打开并校验联合包”，选择本工具的 V1 BIN。通过时结果区显示包信息和目录；文件不受支持、损坏、截断、长度不符或校验失败时显示原因。

ESP32 固件超过 2.5 MiB，或整个包超过 6 MiB 时拒绝打包。PFC、LLC、M4、PLC 超过各自建议预算时会提醒，但满足总大小限制时仍可生成。数据读取期间输入文件发生变化、输出不可写、与输入相同或生成中途失败时不会报告成功；生成先写同目录临时文件、校验后再发布到目标路径。

界面显示的整包 MD5 按包头 MD5 字段置零规则计算，**不同于直接对最终 BIN 执行普通 `md5sum`**。MD5 只检查数据一致性，不验证发布者身份。

## 交付内容

- `dist/FirmwarePackager.exe`：单文件图形程序。
- `src/`：打包、校验和 Windows Forms 界面源码。
- `docs/使用说明.md`：面向操作人员的简明说明。
- `docs/V1格式说明.md`：候选格式的字节布局、校验和扩展规则。
- `test-vectors/`：可复核的 M4 + ESP32 样包、原始输入及预期结果，内容均为合成测试数据。
- `tests/`：组合、异常、GUI 和独立格式核验脚本。
- `docs/验证记录.md`：本次实际构建和测试结果与边界。

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
