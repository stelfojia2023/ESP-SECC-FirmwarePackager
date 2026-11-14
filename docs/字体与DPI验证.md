# 字体与 DPI 清晰度验证

## 本轮范围

批准效果图是位图，没有字体元数据，不能据此认定精确字体。字体更换和放大必须先比较实际安装字体与控件容纳能力，不能把生成图片当原生 Windows 效果。

本轮先采用保守的 System DPI aware manifest，并由 build.ps1 编入 EXE：在启动时的系统 DPI 下由程序绘制，减少 Windows 对 DPI-unaware 整窗的位图缩放。没有声明 PerMonitorV2，也不承诺跨不同 DPI 显示器保持清晰。系统 DPI 更改后应重启程序；真实用户机器效果仍需确认。

当前字体和字号尚未修改：正文 Microsoft YaHei UI 9.5 pt；按钮9 pt；顶部页签10.5 pt；类型12 pt加粗；区标题11 pt加粗；包头Consolas10 pt。Font构造默认单位为Point，不是pixel。字体缺失会由Windows/.NET替代，缺字也可能回退。不得根据请求字体名称假定实际安装。

## 检查证据

TypographyTests枚举实际安装的Microsoft YaHei UI、Microsoft YaHei、DengXian、SimHei、Segoe UI，缺失项明确标缺失，绘制可用字体的中文同文样张，记录控件实际Font.Name/Size/Unit和测试进程DPI awareness。

同一测试可用有/无manifest编译，比较DPI awareness；它在测试进程加载应用窗体，不能代替独立启动生产EXE的系统DPI实测。原生控件DrawToBitmap亦不是桌面截图。125%/150%图片或手动控件缩放不应称Windows显示设置切换实测。

需要的后续验收：真实Windows在100%、125%、150%分别启动EXE，检查文字、输入框、按钮及日志不裁切；移屏/改缩放后重启再看。不得通过改变业务布局或削减功能来通过字体测试。

## 官方依据

- https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows
- https://learn.microsoft.com/en-us/windows/win32/sbscs/application-manifests
- https://learn.microsoft.com/en-us/dotnet/desktop/winforms/high-dpi-support-in-windows-forms

## 主线验证结果与现行规范

本项已合入主线 f97b38402e31b68fce32c7cabe24ebb90f845596。Windows CI 43591687847通过，独立启动生产EXE并查询到System DPI aware。CI实际96 DPI；微软雅黑UI已安装，不属于缺失字体导致的家族回退。用户反馈本版清晰很多，但没有实测其显示配置。字号没有放大，125%/150%系统显示设置切换仍未验证。后续新增界面请遵循[字体与文字清晰度规范](字体规范.md)，不要把字体对照样张的候选字号当作生产现状。
