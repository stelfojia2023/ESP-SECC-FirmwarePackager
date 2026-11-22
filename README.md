# 固件打包工具

将 PFC、LLC、M4、PLC、ESP32 中的 1～5 份固件打包成一个 BIN，支持查看包头和上传 FTP。

源码版本：V0.0.9（未发布）。

[下载 V0.0.8 EXE](https://github.com/stelfojia2023/ESP-SECC-FirmwarePackager/releases/download/v0.0.8/FirmwarePackager.exe) · [下载测试 BIN](https://github.com/stelfojia2023/ESP-SECC-FirmwarePackager/raw/refs/heads/main/test-vectors/five-test-bins.zip) · [文档目录](docs/README.md)

支持 Windows 10/11 64 位，依赖系统的 .NET Framework 4.x。

## 使用

1. 拖入或选择固件，检查名称和版本。
2. 选择输出位置，点击“包生成”；生成后可查看包头。
3. 如需上传，填写 FTP 信息并点击“上传到 FTP”；也可打开 FTP 目录查看文件名。

顶部色圈可切换七种界面配色，首次使用默认银色。

## 注意

- V1 是待评审的候选包格式；设备升级页面尚未接入本地或远程升级接口，不能用于实际升级。
- 普通 FTP 明文传输；上传成功表示服务器确认接收，不代表设备已升级。

详细规则见[使用说明](docs/使用说明.md)、[功能规格](docs/功能规格.md)和[联合包格式说明](docs/V1格式说明.md)。
