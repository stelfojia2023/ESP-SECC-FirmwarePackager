# 固件打包工具：项目协作入口

本仓库是 Windows 10/11 的独立固件打包工具；不是 ESP-SECC 设备固件仓库。先检查当前分支、用户已有改动及本次授权，避免覆盖其他工作。

## 按任务读取

| 任务 | 入口 |
| --- | --- |
| 功能、限制、FTP、日志或升级页 | [功能规格](docs/功能规格.md) |
| 联合包字段、偏移与校验 | [V1 格式说明](docs/V1格式说明.md) |
| 字体、配色与布局 | [字体规范](docs/字体规范.md)、[视觉规范](docs/视觉规范.md)、[七色配色规范](docs/七色配色规范.md) |
| 测试范围、用例和命令 | [测试用例](docs/测试用例.md) |
| 发布步骤、元数据校验和失败处理 | [发布流程](docs/发布流程.md) |
| 交付给操作人员的说明 | [使用说明](docs/使用说明.md) |

规格中“当前实现”“已确认目标”和“待确认”分别处理。V1 是候选包格式，包内偏移不是芯片烧录地址；本地和远程设备升级接口尚未接入。模拟 FTP 或软件测试不能证明真实服务器、平台或设备升级成功。

## 修改与交付

- 保持已有领充机器人图标；用户明确要求改变时再单独评审。界面保持紧凑，字体和七色规范与实现同步。
- 真实 FTP 凭据不写入文档、测试数据或日志。用户已接受默认密码随单文件 EXE 提供；构建及注入方式尚待设计，按功能规格处理。测试使用合成数据和本机模拟服务。
- 本机测试版 EXE 交付至 `D:\zhangyy\Downloads`；WSL 对应 `/mnt/d/zhangyy/Downloads`。目录不可用时说明，不覆盖同名的不同文件。
- `dist/FirmwarePackager.exe` 已纳入仓库并保存最近正式版；构建会覆盖它。普通 PR 的新版本通过构建及测试产物验证，不把旧 EXE 称为新版本；发布时更新并核对版本、来源及 SHA-256。
- 本地测试、Git 提交、PR 合并和 Release 各按用户本次授权执行；普通文档或代码提交不自动发布 Release。

## PR 规则

1. 一个 PR 聚焦一个需求或缺陷；列清相关文件和行为，保留无关改动。
2. 描述触发场景、变更后的行为、实跑测试与结果；未运行或依赖真实 FTP／设备的项目写明原因和边界。
3. 按[测试用例](docs/测试用例.md)选择受影响测试；新增或修复行为应有能发现该行为的测试，避免只复制实现细节。
4. GitHub PR 检查按改动选择相关测试；未知文件、构建或 CI 规则变更自动回退全量。检查结果中跳过的项目不能称为通过。
5. PR 涉及包格式、FTP、UI 或版本时，同步对应规格和使用说明；候选规则不能写成设备端已实现功能。
6. 每个 PR（包括文档、测试和 CI）相对最新 main 的工具版本，将最后一位加 1，例如 `0.0.4 → 0.0.5`；同一 PR 内的追加提交不重复升版。并行 PR 合入前以最新 main 重算。窗口、AssemblyVersion、AssemblyFileVersion、app.manifest、README 和测试断言保持一致，PR 中写明旧版本 → 新版本；工具版本与包格式版本独立。

## 构建、测试与发布

- Windows 10/11 64 位使用系统 .NET Framework 4.x；运行 `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` 生成 `dist/FirmwarePackager.exe`。图标和声音已嵌入，单独 EXE 可运行；测试还需 Python 3.10 及以上。
- 本地选测：`python .github/scripts/select_tests.py --base <基线提交>` 给出建议组；`powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\run-selected.ps1 -Groups <逗号分隔的组>` 执行。选测脚本含未提交和未跟踪文件；无法确定范围时运行全部自动组。
- 涉及真实窗口、拖拽或 FTP 窗口交互时，在已解锁的 Windows 上对 `desktop` 组增加 `-RunDesktop`；图标比对还需 `-BaselineExe <基线EXE>`。GitHub CI 只编译桌面测试，未执行的交互测试须在 PR 中标明。
- 文档检查：`python tests/check_docs.py`、`python tests/test_check_docs.py`；选测规则检查：`python tests/test_select_tests.py`；版本检查：`python tests/check_version.py --base <完整基线提交 SHA>`、`python tests/test_check_version.py`；发布元数据检查：`python tests/check_release_ready.py`、`python tests/test_check_release_ready.py`。
- 发布前执行全部自动组及 `tests/run-tests.ps1`。后者需已解锁的 Windows 交互桌面，会打开窗口并操作鼠标；失败时用 `-Only <用例>` 定向重跑，WSL 可用 `bash tests/run-tests-from-wsl.sh`。输出中的 `core results` 还需用 `tests/independent_verify.py` 独立核验。完整步骤见[发布流程](docs/发布流程.md)。真实桌面回归由操作者完成并留证，GitHub CI 不自动核验该证据。合成 `test-vectors` 不可用于设备升级。
- 一个版本内的准备、验证和修正提交可共用目标版本；README 说明当前版本及可取得的产物，验证记录只对实测版本负责，不改写旧结果。
- `.github/release.json` 记录最近一次 Release 的版本与 EXE 摘要，不是工程版本权威来源；发布时须与目标版本和 EXE 一致。发布前核对源码提交、测试结果、EXE 版本及 SHA-256。
- 主分支提交信息以 `Release v` 开头会触发自动发布；仅在用户明确要求发布、完成前述验证且发布元数据齐备时使用。已有 Release 资产不得被误认为新版本或擅自覆盖。

## Agent skills

### Issue tracker

规格和工作项使用 GitHub Issues；见 [Issue tracker](docs/agents/issue-tracker.md)。

### Triage labels

分类状态使用项目的五个默认标签；见 [Triage labels](docs/agents/triage-labels.md)。

### Domain docs

项目采用单一领域上下文；文档入口及版本兼容规则见 [Domain docs](docs/agents/domain.md)。
