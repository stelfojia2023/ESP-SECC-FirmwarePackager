# V1 样包和测试向量

这里的输入是人工合成数据，**不是设备固件**，不得用于设备升级。

- `inputs/M4.bin`：8193 字节。
- `inputs/ESP32.bin`：4095 字节。
- `bundle-m4-esp32-v1.bin`：20,480 字节；M4 从 `0x001000` 开始，ESP32 从 `0x004000` 开始。
- `expected.json`：名称、版本、偏移、大小、单固件 MD5、整包置零规则 MD5、普通文件 MD5 和 SHA-256。

使用 Python 3.10 或更高版本，在工程根目录执行：

```text
python tests/verify_vector.py test-vectors
```

样包整包 MD5 是 `ef5cd8f46bc048a8a202867259a0ae29`；直接对最终文件计算的普通 MD5 是 `a93fb91832c2b79ac2ad9cfddb0e402c`，两者校验范围不同。
