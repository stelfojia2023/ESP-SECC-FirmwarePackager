# 固件打包工具

将 PFC、LLC、M4、PLC、ESP32 中的 1～5 份固件打包成一个 BIN，支持查看包头和上传 FTP。

源码版本：V0.0.11（候选，未发布）。

[下载 V0.0.10 EXE](https://github.com/stelfojia2023/ESP-SECC-FirmwarePackager/releases/download/v0.0.10/FirmwarePackager.exe) · [下载测试 BIN](https://github.com/stelfojia2023/ESP-SECC-FirmwarePackager/raw/refs/heads/main/test-vectors/five-test-bins.zip) · [文档目录](docs/README.md)

支持 Windows 10/11 64 位，依赖系统的 .NET Framework 4.x。

## 使用

1. 拖入或选择固件，检查名称和版本。
2. 选择输出位置，点击“包生成”；生成后可查看包头。
3. 如需上传，填写 FTP 信息并点击“上传到 FTP”；也可打开 FTP 目录查看文件名。

顶部色圈可切换七种界面配色，首次使用默认银色。

## 注意

- V0.0.10 接入本地 LUP1 升级，尚未完成真实设备联调；远程运营平台升级仍未接入。新版升级功能目前仅供联调，设备收到并校验通过不等于安装成功。
- 普通 FTP 明文传输；上传成功表示服务器确认接收，不代表设备已升级。

详细规则见[使用说明](docs/使用说明.md)、[功能规格](docs/功能规格.md)和[联合包格式说明](docs/V1格式说明.md)。
