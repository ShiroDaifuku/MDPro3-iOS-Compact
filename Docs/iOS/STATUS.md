# MDPro3 iOS 精简版：当前交付状态

本次交付是移植代码和可验证的构建入口，尚不是可安装客户端。没有生成或在 iPhone 验证 IPA，也没有向任何 GitHub 仓库上传。

## 固定基线

- MDPro3：`e177cd8d53092ae9249e37b38d3cef0040e41095`，上游 `sherry_chaos/MDPro3`。
- 目标 Unity：`6000.0.24f1`；本机实际安装并用于脚本编译的是 `6000.0.28f1`。已恢复项目版本文件，未把一次本机导入当作版本升级。
- 原生决斗内核、libevent、Spine 和文件选择器固定提交，见 `Tools/Compact/prepare_native.py` 与 `prepare_dependencies.py`。原先缺失的 Meisui 随机数依赖改为标准 MT19937 首输出，并有参考向量测试。
- UPM 的三个 Git 依赖已固定为这次实际导入的提交。外部默认资源的 SHA-256、长度及版本记录在 `resources.lock.json`。

## 已实现

| 项目 | 当前代码行为 | 验证边界 |
|---|---|---|
| 卡图按需下载 | 先本地、再独立缓存、缺图才请求 CDN；最多 3 个并发；下载完成更新已有卡面纹理 | 缓存和输入校验已测试；Unity 中的下载、裁图和实时替换待实机 |
| 占用控制 | 基础 `.app` 上限 450,000,000 字节；卡图缓存最多 300,000,000 字节；按现有占用进一步缩减缓存，900 MB 为运行目标 | 尚未测得真实 `.app`；iOS 设置的实际占用必须再测 |
| 离线卡图 | 最近发送的卡组 Main / Extra / Side 卡图固定保留并预取；设置里的清空卡图操作只删非固定缓存 | 固定状态与 LRU 重启测试通过；下载成功前不保证离线有图；多组收藏管理尚未实现 |
| 先行卡 | 保留原 YPK 文件名及加载优先级；临时下载官方包，再仅保存数据库、Lua 和文本，替换失败保留旧包；下载 45 MB、解压数据 20 MB 上限 | 人工样例已测；真实包及联网更新见后续测试记录 |
| MyCard | 保留上游登录、匹配、房间、协议处理代码；连接超时和线程停止去掉 `Thread.Abort` | 尚未登录或匹配测试 |
| 离线 Bot | 原 WindBot 和牌组保留；新增原生静态库配方；服务器、Lua、SQLite 和内核合并一份 | Windows 原生链接和三次启停通过；没有证明完整 Bot 对局 |
| iOS AOT | 原生入口改为 `__Internal`；真实反向回调标记；保留反射 AI 类型；新增无需托管回调的只读 SQLite 查询接口 | 新查询接口已读真实中文卡库；IL2CPP / arm64 仍待验证 |
| 精简外观 | 固定默认场地、墓地、底座、卡套和头像框；关闭桌宠对局展示、动态壁纸和特殊终结动画；初始关闭重型特效 | 保留 MDPro3 交互框架；各外观设置及缺资源降级仍需界面回归 |
| 自动构建 | 手动触发工作流：独立测试 → 原生库 → iOS Unity 导出 → Xcode 无签名构建 → 体积闸门 → IPA / 日志 artifacts | 工作流尚未在 GitHub 执行 |

资源清单含 22 个默认资源包，总长度 **124,616,356 字节**；本地文字、脚本与 Bot 数据包 **36,625,668 字节**、523 个文件，卡图为 0。两项均不含 Unity、IL2CPP、Addressables、原生代码和首次启动的数据副本，不能把约 162 MB 当作安装后总占用。大资源下载曾出现连接中断；构建前必须完整获取并通过哈希检查。

900 MB 是应用内预算，额外留下 100 MB 余量；不是已证明的操作系统级硬限制。初次数据复制、先行卡原包/精简包重叠、用户导入、系统磁盘块分配与长时间回放仍要纳入实测。应用不会为腾空间删除用户卡组或回放；“文件”App 外部放入任意大文件也不受缓存配额控制。

## Windows 上已执行的验证

- Unity 6000.0.28f1 真实项目脚本编译通过，日志结尾退出码 0。未运行游戏场景或导出 iOS。
- 进一步使用实际 Unity 编译响应文件切换至 iOS player / IL2CPP 条件符号，去掉 Editor 和旧 SQLite 引用，整个应用 C# 编译通过；由此发现并修复两处误用 Android 专属 BetterStreamingAssets 的 iOS 分支。这仍使用 Windows 引用程序集，不是实际 IL2CPP 验证。
- 独立生产代码测试 **37 项通过**：LRU、固定状态、重启、中断清理、并发预留、流写入限额、JPEG 尺寸、去图 YPK、路径检查、MT19937、真实卡库中文查询及禁止写入。
- Clang 22 / Zig 编译并链接原生服务器和决斗内核；本机回环监听连续三次启动/停止/重启及一次快速停止握手通过，结构长度 80、协议 `0x1362`。这是主机测试，不是 iOS 二进制。
- Windows Zig 的 libevent 头文件需要本地 `_WIN32_WINNT` 兼容调整；该调整没有作为 iOS 源码补丁发布。常规 CI 走 Linux / macOS Clang。
- IPA 打包器另有 3 项测试：Payload 路径、超大 `.app` 拒绝产物、拒绝模拟器包。测试使用人工目录，不是生成了真实 IPA。

复跑独立测试（本机 Unity 的 Mono 宿主是 32 位，因此运行阶段使用已有的 64 位 .NET 8）：

```powershell
python Tools/Compact/run_tests.py --mono-root 'C:/Program Files/Unity/Hub/Editor/6000.0.28f1/Editor/Data/MonoBleedingEdge' --dotnet 'C:/Program Files/dotnet/dotnet.exe'
```

## 下一次构建

1. 获取完整上游工程及本次补丁；不要把稀疏检出的工作目录误当完整可上传工程。
2. 运行 `python Tools/Compact/prepare_dependencies.py`、`prepare_native.py` 和 `prepare_resources.py`，然后运行 `verify_resources.py`。后者对缺包、哈希错误、多余整库直接失败。
3. 使用 `.github/workflows/compact-ios.yml`。它只手动触发，产物不发布 Release。Unity 自动化许可需单独配置；已有 Apple 手机签名流程可继续沿用，不需要把 Apple 签名证书放进本工作流。
4. 在 GitHub 或其他 macOS 构建机上完成 iOS 构建，下载 unsigned IPA 后走已有手机签名安装流程。

工作流采用 [GameCI Unity Builder](https://game.ci/docs/github/builder/) 的 macOS 构建入口；它需要有效 Unity 激活配置。尚未验证本账户许可及 GitHub runner 对固定 Unity 版本的安装，不能承诺首次工作流即成功。无需为编译专门购买个人 Mac。

云端使用当前仍受支持的 `macos-15-intel`，见 [GitHub runner 规格](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)。构建日志记录实际 Xcode 版本，避免沿用已退役的 macOS 14 镜像。

## 必须在 macOS / iPhone 补齐

见 `DEVICE_CHECKLIST.md`。优先级依次为：iOS 原生链接 → 无图启动和文字搜索 → 缺图下载和缓存 → 飞行模式 Bot 完整对局 → MyCard → 先行卡 → 长期占用。
