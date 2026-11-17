# ESP-SECC V1 测试样包

这里的输入是合成数据，**不是设备固件**，不得用于设备升级。样包由打包工具生成，使用 `0x28` 目录布局；旧 `0x80` 布局的包不兼容。

- `inputs/`：五类独立测试输入，大小分别为 PFC 1、LLC 4,097、M4 8,193、PLC 137、ESP32 4,095 字节。
- `bundle-m4-esp32-v1.bin`：两类样包，20,480 字节；M4 偏移 `0x001000`，ESP32 偏移 `0x004000`。
- `bundle-five-v1.bin`：五类样包，36,864 字节；依次从 `0x001000`、`0x002000`、`0x004000`、`0x007000`、`0x008000` 开始。
- `expected.json`、`expected-five.json`：独立核验所需的字段、源文件摘要及整包摘要。
- `five-test-bins.zip`：用于界面手动选择的另一组五类合成输入，不是联合包。

在工程根目录使用 Python 3.10 或更高版本执行：

```text
python tests/verify_vector.py test-vectors
```

两类样包的置零字段整包 MD5 为 `869b01d9d31d853143b29776e742fa39`，最终文件的普通 MD5 为 `40d90ccd1e05eb6830cb80997f1dc137`；两者计算范围不同。五类样包的置零字段整包 MD5 为 `2c1f1b811a0a0c21be2ca1e2c6aff587`。
