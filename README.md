# Unity Yachiyo

把 [Yachiyo Live2D Studio](https://github.com/redchenk/yachiyo-live2d-studio) 的直播导演、对话、弹幕、流式语音和演唱系统接到 Unity 八千代角色。

日常只打开 **Yachiyo Motion Studio.app**（安装到 `~/Applications/`）。界面沿用原导演台：左侧导航、中间角色、右侧控制栏、底部字幕。中间角色由 Unity 渲染，本地导演服务随主应用自动启动和关闭，不需要另外打开 Electron 或终端。

当前统一应用的打包目标是 **macOS Apple Silicon**。Windows／Ubuntu 继续作为可切换的 GPU 动作服务端；本次没有制作 Windows 统一客户端。

## 已接入

- 保留原导演台的观众队列、人格、记忆、LLM 控制、字幕、TTS 和 RVC 演唱流程。
- Unity 接收语义动作、情绪、眨眼、视线及原始 A/I/U/E/O 口型，不经过 Cubism 参数转换。
- 实际音频开始播放时才启动表演；口型和 StreamTalk 身体动作跟随音频时钟。暂停关闭口型并保持姿态，恢复继续；结束和取消清除旧动作。
- Mac 内嵌播放器会逐句接收并补全 GPT-SoVITS 流式 WAV 的长度信息，再开始播放；可预取下一句。停止会取消尚未完成的音频下载。独立 Chromium 调试入口保留边接收边播放。
- 说话时默认使用 StreamTalk，根据实际语音生成身体和手指动作；「角色」页可关闭此功能。其他身体动作走现有模型服务；「角色」页可以明确选择本地预览。挥手、鞠躬、转身、走路、鼓掌、跳舞可请求模型；AI 明确选择的点头、轻微摇头和交谈手势使用本地执行器；禁止自动思考歪头。
- 回执包括 accepted / started / completed / cancelled / unsupported / failed。模型失败会显示失败，不会悄悄改播预览。
- session、epoch、commandId、seq 和 utteranceId 隔离过期命令。取消、切换服务、断线重连不补播旧动作。
- 隐藏、锁定、重置作用于真实 3D 场景。角色区域普通拖动移动位置，右键或 Shift + 拖动旋转，滚轮缩放。「角色」页也提供上下左右按钮、滑杆和缩放；构图位置与视角自动保存。
- 右侧「角色」页可选择台式机 Windows（AMD）、笔记本 Windows（NVIDIA）或笔记本 Ubuntu；分别保存连接配置。
- 本次保留现有肩袖、布料、面部光照与角色资产，没有重新修改服装结构。

## 本机启动

双击根目录的 `启动八千代.command`，或打开 `~/Applications/Yachiyo Motion Studio.app`。首次在齿轮设置中填写自己的 LLM／TTS 服务；账号配置、音频库、记忆和模型权重不进入 Git。

对话 LLM 支持兼容的服务商 API。默认身体动作使用 DiP 原本训练时的 DistilBERT 编码器，不依赖 Llama 审核。通用聊天 API 不等同于 Kimodo 训练所需的文字特征编码器。

远端动作服务仍可手动开关，不设置系统开机自启。Windows 安装、开关脚本在 `windows/`。原动作服务说明保存在 [docs/motion-studio.md](docs/motion-studio.md)。

## 源码构建

需要 macOS、Xcode 命令行工具、Unity **6000.6.4f1**、Node 24 与 Python 3。角色原包须自行提供；公开仓库不包含角色模型、贴图、私有场景和成品应用。

1. 把合法取得的 `.unitypackage` 放进根目录 `月见一万代/`。
2. 运行 `python3 tools/prepare_assets.py`，首次在 Unity 执行 **Yachiyo → Create Studio** 生成私有场景；已有本机场景无需重建。
3. 在 `director/` 执行 `npm ci`。
4. 回到根目录执行 `bash tools/build_unified.sh`。

构建脚本生成导演前端、编译原生 WebKit 插件、下载并校验固定 Node 运行时、构建 Unity、复制运行资源并验证本地签名。角色资源只出现在本机私有成品中。

`director/` 是原导演台 v0.2.6 的源码快照，来源提交 `7cfc08a328be332d3b876a4257e74f8411f08d5f`；来源与 MIT 许可保留在 `director/UPSTREAM.md`、`director/LICENSE`。原 Live2D 模式保留为独立开发调试入口，需另行提供自己的模型资源。

## 验证

```sh
cd director
npm run build
npm run test:desktop
# 单窗口应用：实际 WKWebView 音频、Unity 网格、取消和退出清理
npm run verify:unified
# 安装包实际直播页面：GPT-SoVITS 空长度 WAV 的分块返回、播放与口型
node tools/desktop/verify-tts.mjs
# 可选：连接自己的 GPT-SoVITS，配置文件不入库，LLM 回复使用本地固定样例
node tools/desktop/verify-tts.mjs --settings-file /absolute/path/private-tts-settings.json
# 独立协议调试，可选连接真实 GPU 服务
YACHIYO_VERIFY_MODEL=1 npm run verify:unity
```

```sh
# 不需要 GPU 权重的动作服务逻辑检查
python3 tools/fetch_motion_sources.py --skeleton-only
python -m unittest discover -s tests -v
```

测试音频是可复现的合成元音，不调用收费 LLM／TTS，也不发送真实弹幕；这不等同于真人声音听感验收。Unity 运行检查需要本机已导入角色并完成构建。产物保存在不入库的 `artifacts/`。

协议与生命周期见 [docs/avatar-bridge.md](docs/avatar-bridge.md)，第三方来源见 [THIRD_PARTY.md](THIRD_PARTY.md)。

## 本地动作与头发

- 本地动作执行器改成带淡入淡出的多关节表演：挥手会带动肩、上臂、前臂、手腕、胸、颈、头和髋部重心；鞠躬、点头、摇头、转身也分别有躯干/颈部分担，不再是单根骨骼的固定角度。动作结束自动回待机。
- 新增 `HairPhysics`：自动识别原模型的 `Acs_Twintail_*`、`Acs_SideHair_*`、`Acs_FrontHair_*` 链（当前 10 条链 / 32 根骨骼），用头部角速度做弹簧-阻尼滞后，并带轻微自然摆动；不接管身体动作，也不改头发网格。
- 导演台动作的提交/取消/完成仍走 `DirectorBridge`：`perform` → `avatar.Act`（本地）或 `ui.RequestMotion`（动作服务）；取消走 `CancelPerformances` / `avatar.Act("idle")`；完成由 `Execute` 的持续时间和 `Receipt("completed")` 返回。Unity 端每帧执行，不存在批处理式迟到。
- 动作服务（DiP/远端）返回的动作仍取决于服务本身；本地动作现在用于「角色 → 本地动作预览」和不需要远端服务的微动作。

## 对话时的角色表演

- 3D 模式直接忽略语音加载和回复生成期间的 `thinking` 通知，不在每句开头插入准备／思考动作；排队语音不再额外加入回复间隔。`listening` 等状态只表示交互进度。统一应用关闭鼠标驱动头部；相机拖动与角色位置控件仍可手动使用。
- StreamTalk 使用最终实际播放的音频生成动作，不叠加循环的手臂、胸、颈或头部动作。关闭 StreamTalk 时，音频只提供口型与表演时钟。`performance.gestures` 缺省、为空、格式错误或只有禁用动作时，均不补默认手势。待机呼吸、眨眼与布料物理仍独立运行。
- LLM 在同一次回复中输出 `performance`：安慰、感谢、解释、打趣、惊讶、克制拒绝或倾听。持续情绪在会话内跨句保留、随时间衰减，并进入下一次回复的提示；规则降级避免把“没有生气”“别哭，我陪你”演成愤怒或哭泣。
- 表情、眼神、语速、身体幅度共用这份计划。关闭 StreamTalk 时，`gestures: []` 表示不执行局部手势，保留眨眼、呼吸和轻微姿势变化；安慰时使用关切的柔和表情，感谢时有短暂移开视线。
- `explain`、`open_palms`、`emphasis`、`nod`、`shake_head`、`lean_in` 等交谈手势只有 AI 明确输出时才在 Unity 就地执行，有准备、表达和收回阶段。身体和头部可同时执行，动作不再拉伸到整句长度。完整模型动作继续使用现有 Windows／Ubuntu 服务。
- 一句语音结束后，属于该句的延迟动作不会继续排队播放。模型播放期间不叠加本地头部或手臂手势，避免破坏生成姿态。
- 表情和视线在音频实际开始时生效，句末回到较淡的当前情绪，而非立即清空。口型继续走 `YachiyoFaceController.SetAudioMouth`。
- 直接输入新消息或点麦克风会取消旧回复、音频预取和旧动作。全程语音输入可勾选「允许语音插话（建议戴耳机）」；默认仍防回声，勾选后用浏览器回声消除后的持续语音能量触发打断。麦克风仍需手动开启。

当前对齐使用合成音频的起音与真实播放时钟。文本锚点只有在具备最终日语文本的可靠词时间时才执行；本项目尚未部署强制对齐服务，不用字符数估算冒充词级同步。GPT-SoVITS 已接入 0.9–1.1 倍 `speed_factor`；保留原参考音频与其转写，不把情绪指令塞入 `prompt_text`，也不声称它支持独立音高控制。情绪语音的音色和听感仍需参考音频标定与真人试听。

表演计划实机回归：`node director/tools/desktop/verify-performance.mjs`。实际页面插话回归：`node director/tools/desktop/verify-interruption.mjs`。两者使用固定回复/合成音频，结果保存在本机 `artifacts/performance-validation/`。

短句动作与位置控件的实机回归：`node director/tools/desktop/verify-conversation.mjs`（先构建并安装统一应用）。

## StreamTalk 语音驱动动作

已接入实际 StreamTalk 网络：最终 TTS 音频 → WavLM → 动作生成 → 检索关键姿态 → 动作细化 → Unity 身体和手指。脸部情绪继续由对话计划驱动，口型继续来自实际音频；没有把 FLAME 的 100 维输出直接冒充八千代表情。

每个模型窗口为 60 帧、30 FPS，保留 8 帧用于与下一段融合。首段准备好后播放语音，后续分段生成；当前句播放期间，同时预取下一句语音并预生成它的首个动作窗口，切句时复用已有缓冲，不再重新提交同一任务。只提前准备下一句动作，最多占用两个 GPU 任务槽；取消、队列过期、优先级变化或切换电脑会清理对应预取。Unity 以音频媒体时间采样动作。暂停保持姿态，恢复接续；取消、切换系统和断线丢弃过期帧。若生成赶不上播放，会保持最后一个生成姿态并记录欠载，不会用预设动作填充。

笔记本 Windows 的 DiP 与 StreamTalk 共用 `E:\YachiyoMotion` 的同一启动/关闭入口、同一服务端口和串行 GPU 队列。StreamTalk 的新增 Python 包隔离在 `runtime-streamtalk`，原 DiP 运行时保留。服务启动时预热；无需再手动启动另一个进程。Mac 仍是一个应用。Ubuntu 切换入口保留，Ubuntu 的 StreamTalk 安装和实机验收尚未完成。

首次准备：

1. 运行 `python3 tools/fetch_streamtalk.py` 下载固定版本的源码、speaker-2 权重、WavLM 和 95 份 BEAT2 检索动作。
2. 从 [SMPL-X 官网](https://smpl-x.is.tue.mpg.de/) 取得授权后，将 `SMPLX_NEUTRAL_2020.npz` 放入 `models/streamtalk/`。
3. 将 `vendor/streamtalk`、`models/streamtalk`、更新的 `service/` 与 `windows/` 复制到笔记本既有 `E:\YachiyoMotion`。用原运行时执行 `runtime\python.exe windows\install_streamtalk.py`，再通过现有入口重启动作服务。
4. Mac「角色」页选择「笔记本 Windows · NVIDIA」，保持「StreamTalk 语音驱动动作」开启。模型资源与检索缓存不进入仓库或 Mac 安装包。

3070 Laptop 8 GB 的首轮实测：6 秒实际人声产生 180 帧，首段约 2.11 秒、后续窗口约 1.3–1.4 秒；StreamTalk 单独进程的 PyTorch 显存峰值约 1599 MiB。与 DiP 共用服务时实测峰值约 1972 MiB。这个数字不包括驱动、桌面和其他进程，也不是端到端聊天响应时间；实际性能取决于 GPU 负载和语音长度。

真实 GPU/原生播放联调（需先启动笔记本服务并建立 SSH 隧道）：

```sh
cd director
node tools/desktop/verify-streamtalk.mjs /absolute/path/spoken.wav
```

它只用 WAV 代替 TTS 返回，身体动作必须由当前 GPU 服务实时生成，检查播放时钟、动作变化、暂停、恢复和打断。测试音频与私有模型不入库。此模型的公开 speaker-2 检索风格并非针对八千代或日语专门训练，动作风格仍需实际对话试听与观察。

2026-11-13 实机验收：实际安装页面完整播放 6 秒人声，收到 293 个口型/媒体时钟消息；单独的原生链路验证了神经动作、暂停保持、恢复和取消，记录点无动作欠载。固定随机种子时，人声与静音输入的输出姿态不同，确认音频条件确实参与生成。当前 GPT-SoVITS 地址连接失败，本轮使用官方真实人声 WAV 验证动作，不等于已完成当前语音服务或日语音色验收。

性能更新：袖口沿用原始绑定和权重，仅计算 40 个衔接点，移除每帧六次整件衣服的网格烘焙，并复用袖摆顶点缓冲。M5 Mac 在相同 2940×1584 窗口的 20 秒采样中，布料更新平均由 3.21 ms 降至 2.09 ms；静止渲染前后均约 60 FPS。StreamTalk 仍生成 30 FPS 动作，Unity 使用跨数据包的平滑轨迹按显示帧采样，并渐进校正语音播放时钟；暂停和打断立即停止。详细验证及复现方式见 [验证记录](docs/validation.md)。

待机与衔接更新：加入缓慢换重心、固定脚底、错开节奏的呼吸与手腕／手指微动，以及不规则眨眼和眼睛先于头部的小幅注视变化；不加“歪头思考”。相邻动作使用当前姿态及角速度衔接；已缓存的下一段直接接入。明确结束或取消时立即平滑收势，约 0.8 秒回到自然站姿。短时计算缺帧先沿真实动作的速度／加速度延续，最多补偿 1.1 秒，再平滑退出；新数据到达时逐渐校正回模型轨迹。暂停语音会同时暂停动作及过渡。待机细节在本地运行，语音肢体动作仍由 StreamTalk 真实生成。32 秒原生采样约 60 FPS；真实 GPU 连续三句的播放、衔接、暂停与打断验证通过。


## 台式机 Windows / AMD

已在 RX 9070 XT 16 GB 台式机部署到 `D:\YachiyoMotion`，使用独立复制的 Python 3.12 / PyTorch 2.9.1 + ROCm 7.2.1 环境。DiP 和 StreamTalk 共用一个动作服务进程；原 GPT-SoVITS 环境与进程保持独立。E 盘空间不足，因此本次使用 D 盘；笔记本的 E 盘副本保留。

- Windows 桌面「八千代动作 - 启动／关闭／状态」可手动管理；不设置开机自启。
- Mac「角色 → 动作服务 → 台式机 Windows · AMD」，或 F6 面板按 3，选择会保存。首次 SSH 登录或会话失效时在本机终端输入 Windows 密码，密码不写入源码或设置。
- 台式机通道为 `127.0.0.1:28787`，笔记本 Windows 为 `18787`，Ubuntu 为 `8787`；三个通道相互独立。
- 重装 AMD 环境时先准备好已有可运行的 Windows ROCm Python，再执行 `windows/install_amd.ps1 -BasePython <python.exe路径>`。脚本仅复制环境并在副本安装动作依赖；模型和 `vendor` 需另外按授权准备，不能用 NVIDIA 安装脚本覆盖此环境。

台式机实测 DiP 每段 2 秒动作约 0.09–0.11 秒；StreamTalk 预热后的首窗口约 0.31–0.32 秒。真实 AMD 推理 → Mac 音频 → Unity 连续两句的交接约 56 ms，采样无动作欠载、无中间待机，暂停／恢复／打断通过。这些是本次样例结果，首次推理更慢；LLM 或 TTS 尚未给出下一句时仍需等待，不能保证所有对话零延迟。

复现台式机链路：

```sh
YACHIYO_VERIFY_BACKEND=desktop node director/tools/desktop/verify-streamtalk.mjs /absolute/path/spoken.wav
```

该回归只替换 TTS 返回音频，身体动作来自真实 GPU，不代表已验收当前在线 TTS 或多服务并发性能。详见 [验证记录](docs/validation.md)。


轨迹与延迟补偿更新：根节点和骨骼旋转改用跨包的三次平滑曲线，减少 20/30 FPS 模型采样点之间的速度突变。DiP 根据请求耗时，在剩余缓冲低于 2.4–5 秒阈值时提前生成；StreamTalk 根据首窗口耗时准备播放缓冲。短时预测只延续已有 AI 轨迹，不生成新的语义动作，也不无限外推。人为造成约 800 ms 动作缺帧的真实 GPU 测试通过，文字动作连续 13 段无欠载，20 秒混合动作采样约 59.6 FPS；详见验证记录。
