# DeepSeek Harness 启动器

一个 Windows 桌面启动器：双击图标即可完成「检查远端 tag → 按需更新源码 →（必要时）`pnpm run build` → `pnpm dsh web`」的全流程，无需再手敲命令。

- 目标仓库：`..\deepseek-harness`（可在 `launcher.config.ini` 中修改）
- 远端：`https://github.com/deepseek-ai/deepseek-harness.git`
- 实现方式：C# 5 + WinForms，由 Windows 自带的 `csc.exe` 编译，**无需安装任何 SDK 或运行时**

## 启动流程

```
双击桌面图标
  │
  ├─ 环境检查（git / node / pnpm 是否存在、源码目录是否存在、单实例互斥）
  │
  ├─ 查询远端 tag（git ls-remote，失败时回退 GitHub API）
  │     └─ 有本地还没有的更高版本 tag？
  │          ├─ 是 → 弹窗「发现新版本 tag，是否更新？」
  │          │        ├─ 是 → 工作区脏？→ 提示暂存/跳过/取消
  │          │        │        → 记录更新前状态 → git fetch --tags --force
  │          │        │        → git checkout --detach refs/tags/<tag>
  │          │        │        → pnpm install --frozen-lockfile（可关闭）
  │          │        │        → pnpm run build（更新后必须构建）
  │          │        └─ 否 → pnpm run build → pnpm dsh web
  │          └─ 否（已是最新）→ 已有当前提交的构建产物？
  │                   ├─ 有 → 跳过 pnpm run build，直接 pnpm dsh web
  │                   └─ 无 → pnpm run build → pnpm dsh web
  │
  └─ pnpm dsh web（默认 127.0.0.1:3080；端口占用时可改用空闲端口）
        └─ 解析输出中的带 token 地址，按 openTarget 打开界面：
             browser = dsh 自己打开默认浏览器（不加 --no-open）
             app     = 启动器执行 appCommand 打开指定应用（自动加 --no-open）
             none    = 不打开，只在窗口上显示地址
```

窗口会一直保留作为运行监视台；勾选「关闭窗口时停止服务并关闭界面应用」（默认勾选）时，关闭窗口会：

1. 向由它打开的界面应用窗口投递关闭消息（只关这一个窗口，绝不结束浏览器进程）；
2. 用 `taskkill /T /F` 回收整棵 `cmd → pnpm → node` 进程树。

## 界面

外观取自 DSH Web 客户端的设计令牌（`packages/client/ui-theme` 的 `--dsw-alias-*` / `--dsw-static-*`）：平台底色 + 白色/深灰卡片层、三级文字颜色、圆角卡片与细边框、状态徽标（圆点 + 胶囊），主按钮用 deepseek 蓝强调色。

- 主题跟随 Windows 的「应用模式」（与 DSH 网页的明暗一致），可用 `theme = auto | dark | light` 指定。
- **紧凑**：默认窗口 940×620，头部按内容占高（默认约 120–133px），底部两行共 62px，其余都给日志区。
- **文字不会被裁剪**：文字控件一律按内容自适应尺寸，长文本（阶段说明、源码路径、界面地址）自动换行并由所在行撑高；窗口缩到最小 660×430 时仍不裁剪。
- **按钮自绘**：从普通 `Control` 派生（不是 `Button`——`ButtonBase` 会在自己的消息处理里再画一圈平面按钮描边，即使设了 `UserPaint` 也会在按钮周围留下深灰"阴影"）；常态/悬停/按下/禁用四色显式定义，并支持鼠标、空格/回车、焦点虚框与无障碍角色。
- 日志区开启自动换行，长命令行不会横向被截断；日志按级别着色（命令=蓝、成功=绿、警告=琥珀、错误=红）。

界面布局与配色由 `--ui-check` 自动验证（见「验证」）。

## 目标源码目录

默认使用 `launcher.config.ini` 里的 `harnessDir`（即 `..\deepseek-harness`）。界面底部有一个**目标目录选择器**（文件夹图标 + 目录名 + 灰色完整路径 + 折角箭头），点它弹出菜单 —— 交互与排版照 DSH 客户端的 workspace 选择器：

```
┌────────────────────────────────────┐
│ 默认目录                            │   ← 分组标题（次要文字）
│  📁 deepseek-harness  D:\...\dsh ✓ │   ← 图标 + 目录名 + 路径 + 当前项打勾
│ 最近使用                            │
│  📁 harness-a         E:\alt\...   │
│ ────────────────────────────────── │   ← 细分隔线
│  ＋ 浏览其他目录…                   │   ← 固定动作
└────────────────────────────────────┘
```

- 菜单可用键盘操作：↑/↓ 移动、Enter 选中、Esc 关闭；点击别处自动收起。
- 切换立即生效并记入「最近使用」（`state\harness-targets.ini`，最多 4 个，默认目录不入列表）。
- 命令行方式：`--harness <路径>`（支持相对路径，脚本/快捷方式用）。

规则与安全边界：

- 切换只影响本次启动，**不会改写配置文件**。
- 目录不存在 → 拒绝启动；存在但没有 `package.json`、或其中没有 `dsh` 脚本 → 提示确认后再用（`--harness` 形式只告警）。
- 自动启动阶段不弹模态框：目录不可用时直接失败并在日志/状态里说明，窗口留着让你换目录。
- **构建记录按目录区分**：换目录后不会误用另一个目录的构建产物（首次会重新构建一次）。
- tag 检查、更新、构建、`dsh web` 全部针对所选目录执行。

## 界面打开方式（`openTarget`）

`pnpm dsh web` 默认会打开系统默认浏览器的一个标签页。需要像桌面应用那样打开时，改 `launcher.config.ini`：

| `openTarget` | 行为 |
| --- | --- |
| `browser` | 由 dsh 打开默认浏览器（原始行为） |
| `app` | 由启动器执行 `appCommand`；web 命令自动追加 `--no-open`，不会再弹出浏览器标签页 |
| `none` | 不自动打开，只在启动器窗口里显示可点击的地址 |

`appCommand` 中可写 `{url}` 占位符，启动时替换为**带 token 的完整地址**，保证首次访问即可登录：

```ini
# 例 1｜Edge“应用窗口”：跟随实际端口、总是带新 token（最稳）
appCommand = "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" --app="{url}" --profile-directory=Default

# 例 2｜已安装的 PWA / 应用：按 app-id 打开
appCommand = "C:\Program Files (x86)\Microsoft\Edge\Application\msedge_proxy.exe" --profile-directory=Default --app-id=<应用 id>

# 例 3｜任意快捷方式或程序
appCommand = "%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\DeepSeek Harness.lnk"
```

关于登录：dsh 会把 URL 里的 `?token=` 换成**持久签名 cookie**（密钥存在 Harness home，跨重启有效）。因此

- 带 `{url}` 的形式每次都带新 token，任何情况下都能登录，也会跟随实际端口；
- 不带 token 的已安装应用（例 2、例 3）依赖该浏览器配置里已有的 cookie，且地址是固定的——例 2 固定指向 `http://127.0.0.1:3080/`，若端口被占用改用了 3081，它仍会打开 3080。需要跟随端口时用例 1。

启动器里的「打开界面」按钮与自动打开使用同一套规则；`--diagnose` 会打印解析后的命令并校验可执行文件是否存在。

### 退出时关闭界面应用（`closeAppOnExit`）

应用窗口由浏览器主进程持有，结束进程会连带关掉你所有的浏览窗口，所以启动器的做法是：**只对识别出的那一个窗口投递 WM_CLOSE**，不结束任何进程。

识别方式：

1. 启动应用前先记录所有顶层窗口句柄作为基线；
2. 启动后在后台（最长约 20 秒）找出新出现的、且不是浏览器主窗口的那个窗口；
3. 退出或点「停止」时关闭这个记录下来的窗口。

因此默认行为是「**只关闭本次启动打开的那个窗口**」。如果你手动把应用窗口关了，启动器不会去找新的窗口关。

如果应用在启动器启动前就已经开着（Edge 会把已有窗口置前，不会产生新窗口），可以配置 `appWindowTitle` 让启动器按标题匹配：

```ini
closeAppOnExit = true
appWindowTitle = DSH 本地构建      ; DSH 网页标题是本地化的：DSH 本地构建 / DSH Local Build
```

安全边界：浏览器主窗口（标题以 `Microsoft Edge`、`Google Chrome`、`Firefox` 等结尾）永远不在关闭范围内——所以即使你的浏览器里正开着 DSH 页面，也不会被关掉。

## 构建判定规则（是否执行 `pnpm run build`）

已是最新时不再重复构建，这是默认行为。判定自上而下，命中即生效：

| 情形 | 是否构建 | 说明 |
| --- | --- | --- |
| `--force-build`，或 `buildWhenUpToDate = true` | **构建** | 强制刷新 |
| 工作区有未提交修改 | **构建** | 产物无法代表当前源码 |
| 存在可用更新 | **构建** | 更新后的新源码必须重新编译 |
| 无法确认远端状态（未开启检查/断网） | 由 `buildWhenCheckFailed` 决定 | 默认 `true` = 保守构建 |
| 已是最新 + 干净 + 已有当前提交的产物 | **跳过** | 直接 `pnpm dsh web` |
| 已是最新 + 干净 + 没有当前提交的产物 | **构建** | 首次启动会构建一次，之后即可跳过 |

「已有当前提交的产物」由两个只读证据判断，任一成立即算有效：

1. 启动器自己的记录 `state\last-build.ini`：该提交被启动器以干净工作区构建成功过；
2. 仓库自己的记录 `.dsh-build\client-build-environment.json` 中的 `DSH_CLIENT_COMMIT_HASH` 与当前 HEAD 一致（因此手动 `pnpm run build` 过也认）。

两者都不确定时一律构建——**宁可多构建一次，也不让 `dsh web` 因产物缺失/过期而失败**。另外，若跳过构建后 `dsh` 仍报告客户端产物与源码不一致，启动器会自动补做一次完整构建并重试一次。

## 目录结构

```
deepseek-harness-launcher/
  launcher.config.ini         配置（INI，带中文注释）
  build.ps1                   编译：src\*.cs → dist\DeepSeekHarnessLauncher.exe
  install.ps1                 安装：编译 + 创建桌面快捷方式（可选开始菜单）
  uninstall.ps1               卸载：删除快捷方式
  run-launcher.cmd            免快捷方式启动（缺 exe 时自动编译）
  src/*.cs                    启动器源码
  tools/New-LauncherIcon.ps1  重新生成 assets\dsh-launcher.ico
  tools/Test-Launcher.ps1     两阶段端到端回归测试（一次性夹具仓库，不碰真实源码）
  assets/dsh-launcher.ico     快捷方式与程序图标（已生成并提交）
  dist/                       编译产物（可随时重新生成）
  logs/                       运行日志与诊断报告
  state/                      更新前状态与最近一次成功构建的记录
```

## 安装与使用

```powershell
# 安装（编译 + 桌面快捷方式）
powershell -ExecutionPolicy Bypass -File install.ps1

# 只想编译
powershell -ExecutionPolicy Bypass -File build.ps1

# 同时创建开始菜单快捷方式
powershell -ExecutionPolicy Bypass -File install.ps1 -StartMenu

# 卸载
powershell -ExecutionPolicy Bypass -File uninstall.ps1
```

不想装快捷方式时，直接双击 `run-launcher.cmd` 也可以。

### 命令行参数

| 参数 | 说明 |
| --- | --- |
| 无 | 打开窗口并自动开始启动流程 |
| `--no-auto-start` | 只打开窗口，不自动开始（先看配置或日志时用） |
| `--yes` / `-y` | 无人值守：自动确认更新、自动改用空闲端口；工作区有未提交修改时**只跳过更新**，不动本地改动 |
| `--force-build` | 本次强制 `pnpm run build`（改过源码但已提交时用） |
| `--allow-multiple` | 允许与已在运行的启动器实例并存（默认单实例） |
| `--diagnose` / `--check` | 只读环境诊断，含构建判定预测，输出 `logs\diagnose-report.txt` |
| `--self-test` | 运行内置逻辑自检（65 项断言） |
| `--ui-check` | 界面自检：尺寸/裁剪/重叠 + 按钮四态对比度 + 像素级黑边检查（不显示窗口、不启动流程） |
| `--harness <路径>` | 指定本次使用的源码目录（默认用配置里的） |
| `--config <路径>` | 指定另外一份 `launcher.config.ini`（可用来指向别的仓库） |
| `--help` | 帮助 |

## 配置说明（`launcher.config.ini`）

| 键 | 默认值 | 说明 |
| --- | --- | --- |
| `harnessDir` | `..\deepseek-harness` | 默认源码目录，相对配置文件所在目录或绝对路径（界面/`--harness` 可临时切换） |
| `remoteName` | `origin` | 更新时使用的 remote |
| `remoteUrl` | GitHub 地址 | remote 缺失时的兜底地址，也用于 GitHub API 兜底 |
| `tagPrefix` | `dsh-v` | 只把以此开头的 tag 当版本 tag |
| `checkUpdates` | `true` | 启动时是否检查远端 tag |
| `port` | `3080` | Web 端口，`0` 表示交给 dsh 自动选择 |
| `installAfterUpdate` | `true` | 更新到新 tag 后执行 `pnpm install --frozen-lockfile` |
| `buildWhenUpToDate` | `false` | `false` = 已是最新且产物有效时跳过构建；`true` = 每次都构建 |
| `buildWhenCheckFailed` | `true` | 无法确认远端状态时是否仍然构建 |
| `gitSslFallback` | `true` | git 访问失败时用 `-c http.sslBackend=openssl` 重试一次 |
| `closeStopsService` | `true` | 关闭窗口时停止 Web 服务 |
| `buildCommand` | `run build` | 实际执行 `pnpm run build` |
| `webCommand` | `dsh web` | 实际执行 `pnpm dsh web` |
| `webExtraArgs` | 空 | 追加到 web 命令后的参数，如 `--trusted-host 10.0.0.2:3080` |
| `openTarget` | `browser` | 界面打开方式：`browser` / `app` / `none` |
| `appCommand` | 空 | `openTarget = app` 时执行的命令，支持 `{url}` 占位符 |
| `closeAppOnExit` | `true` | 退出/停止时关闭由启动器打开的界面应用窗口 |
| `appWindowTitle` | 空 | 可选的界面窗口标题关键字（应对"应用本来开着"的情况） |
| `theme` | `auto` | 启动器窗口主题：`auto`（跟随 Windows 应用模式）/ `dark` / `light` |
| `echoOutput` | `true` | 是否把子命令输出实时上行到界面 |

## 更新判定规则

每个启动周期取「远端最新版本 tag」与本地状态比较，按 SemVer 2.0.0 排序（`alpha < rc < 正式版`，`rc.10 > rc.2`）。满足**任一**条件即提示更新：

1. 本地尚未获取该 tag；
2. 同名 tag 指向的提交与远端不一致（远端移动过 tag）；
3. 远端最新版本高于当前检出的版本（`git describe --tags --abbrev=0`）；
4. 当前不在任何版本 tag 上，而远端有版本 tag。

**不提示**的典型情形：本地已包含远端最新 tag。例如首次运行时的实际状态是
`master @ c291e79`（比最新 tag `dsh-v0.1.5-rc.2` 多 139 个提交），此时不会提示更新，也不会把工作区回退到该 tag。

检查失败（断网、远端不可达）不会阻塞启动：记录警告后按 `buildWhenCheckFailed` 决定是否构建。

## 改动范围

- 回答「否」或本就无需更新时，启动器**只读取**源码目录，不做任何写操作。
- 回答「是」时只执行 `git fetch`、`git checkout --detach <tag>`（以及可选的 `pnpm install`），**不会** `git reset`、`git clean`，也不会覆盖本地未提交修改。
- 更新前会把分支、提交、目标 tag 写入 `state\pre-update-state.txt`；若选择「暂存并更新」，改动保存在 `git stash` 中，可用 `git stash pop` 恢复。
- `--diagnose`、`--self-test` 与 `--ui-check` 全程只读（仅 `ls-remote` 网络查询），可用于随时体检。
- 启动器自身只写 `deepseek-harness-launcher\` 下的 `logs\`、`state\` 与快捷方式。

## 验证

四条可重复执行的验证，全部通过：

```powershell
# 1) 逻辑自检：版本比较、tag 解析、更新判定、构建判定、界面打开方式、窗口识别、配置解析（65 项断言）
dist\DeepSeekHarnessLauncher.exe --self-test        # 退出码 0

# 2) 只读诊断：工具链、仓库状态、远端 tag、端口、构建判定、界面应用与关闭设置、将要执行的命令
dist\DeepSeekHarnessLauncher.exe --diagnose         # 退出码 0，报告见 logs\diagnose-report.txt

# 3) 界面自检：4 种尺寸 + 控件四态对比度 + 像素级黑边检查 + 菜单渲染（166 项检查）
dist\DeepSeekHarnessLauncher.exe --ui-check         # 退出码 0，报告见 logs\ui-check-report.txt

# 4) 三阶段端到端回归（40 项断言：更新构建 / 跳过构建+界面应用 / --harness 切换目录）
powershell -ExecutionPolicy Bypass -File tools\Test-Launcher.ps1
```

`--ui-check` 会构造窗口（不显示）并把界面文字换成**最长可能内容**，在 720×480 / 820×560 / 1000×700 / 1280×820 四种尺寸下逐项检查：

- 每个可见文本控件的测量所需宽高都小于它实际占用的宽高（即文字不会被裁剪）；
- 头部 / 日志区 / 底部互不重叠，日志区高度足够，头部与底部不会过高（紧凑度）；
- 任何控件都不越出父容器；日志区开启自动换行；
- **每个可交互控件（按钮、目标目录选择器）在常态/悬停/按下/禁用四种状态下的前景-背景对比度 ≥ 4.5:1**（WCAG AA），状态徽标同理 —— 这条专门防「控件上的字看不见」；
- **把按钮渲染成位图后逐像素检查最外 2 像素边框**：只能是底色/边框色/父容器底色或它们之间的抗锯齿过渡，四角必须等于父容器底色 —— 这条专门防「按钮四周有黑边/阴影」；
- 真实点击一次「清空」按钮，确认 Click 事件确实接到处理器（按钮已不是 `Button`，事件链路需要证明）；
- 校验目标目录弹出菜单的结构（分组标题 / 默认 / 最近使用 / 浏览动作、当前项打勾）并把菜单渲染成位图逐像素比对配色。

实测：浅色与深色主题下各 **166 项检查、0 项不成立**。

`tools\Test-Launcher.ps1` 在 `test\.run\` 下自建裸仓库 + 目标仓库 + 构建/服务桩程序 + 一个真实的窗口桩程序（现场用 csc 编译），三阶段验证：

- **阶段 A**（远端有新 tag）：发现更新 → 自动更新到 `dsh-v0.1.1`（分离头指针）→ 执行 `pnpm run build` → 桩服务就绪并解析出带 token 地址 → 构建记录指向检出后的提交 → 关闭窗口后进程树被回收。
- **阶段 B**（已是最新，且 `openTarget = app`）：跳过 `pnpm run build` → 直接拉起服务 → web 命令带 `--no-open` → 界面应用桩收到带 token 的地址并打开一个真实窗口 → 启动器识别并记录该窗口 → **启动器退出后该窗口进程消失**；构建标记文件时间未变，证明构建脚本确实没有再次执行。
- **阶段 C**：配置里写一个不存在的目录，再用 `--harness` 指向夹具仓库 → 启动器忽略无效默认值、按指定目录完成启动。

测试不需要网络，不会访问 `deepseek-harness`，也不会关闭其它已在运行的启动器实例（以 `--allow-multiple` 并存）。

> 注意：如果在一个禁止命名管道的受限沙箱里运行，Git for Windows 的本地传输（`sh.exe` 信号管道）会失败，该测试需要正常权限；真实流程走 HTTPS，不受影响。

## 故障排查

| 现象 | 处理 |
| --- | --- |
| 提示「未找到命令 pnpm/node/git」 | 安装对应工具并确认在 PATH 中，然后重开启动器 |
| 提示 `git ls-remote` 失败 | 已自动回退 `openssl` 证书后端与 GitHub API；仍失败时按提示检查网络/代理 |
| 端口被占用 | 弹窗询问是否改用空闲端口；也可修改 `port`，或 `--yes` 自动改用 |
| 双击图标没反应 | 启动器已在运行（单实例），检查任务栏；确实需要并存时用 `--allow-multiple` |
| 想强制重新构建 | 加 `--force-build`，或把 `buildWhenUpToDate` 设为 `true` |
| 界面应用打不开 | 看日志里的失败原因；`--diagnose` 会校验 appCommand 的可执行文件是否存在 |
| 应用窗口显示未授权 | 该应用地址里没有 token 且 cookie 已失效：改用 `--app="{url}"` 形式（见「界面打开方式」） |
| 退出后应用窗口还在 | 说明它不是你这次启动时新打开的：设置 `appWindowTitle` 让启动器按标题匹配后关闭 |
| 不想让启动器关窗口 | 把 `closeAppOnExit` 设为 `false`，或取消窗口底部勾选框 |
| 跳过了构建但 `dsh` 报产物不一致 | 启动器会自动补构建并重试；仍失败则手动 `pnpm run build` |
| 构建失败 | 窗口日志与 `logs\launcher.log` 中有失败命令的末尾输出；按提示手动执行 `pnpm install` 后再试 |
| 想改图标 | 编辑 `tools\New-LauncherIcon.ps1` 后执行 `build.ps1 -RebuildIcon`，再运行 `install.ps1` |
| 想回到更新前的代码 | 按 `state\pre-update-state.txt` 中的说明 `git checkout <分支>`，必要时 `git stash pop` |
