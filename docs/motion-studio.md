> 原动作工作室说明。导演台、语音口型与统一应用的最新入口见根目录 README。文中 artifacts 为本地验证记录，不随公开仓库分发。

# 八千代 Motion Studio

Unity 桌面客户端 + Ubuntu RTX 3070 动作服务。**默认已接入 DiP 连续生成和 Unity 原生 Cloth**，另可切换 ARDY 公共在线 API 生成完整片段。Kimodo 的接口和官方回放样例仍保留。

## 使用

双击根目录 **启动八千代.command**。客户端会恢复上次选择的台式机 Windows／笔记本 Windows／Ubuntu 动作服务；笔记本离线时仍可使用本地预览。

- **场景设置 F6 → Windows／Ubuntu**：选择笔记本当前启动的系统，即可切换动作来源，无需手动改地址。设置面板打开且未编辑文字时，也可按 **1 选笔记本 Windows、2 选 Ubuntu、3 选台式机 Windows**。选择会保存，重启客户端后继续使用。
- 首次连接或登录会话过期时，会打开本机终端完成 SSH 登录；Windows 密码不会写入客户端或脚本。Windows 仍由笔记本上的手动入口启动／关闭模型；若未启动，客户端会明确提示。Ubuntu 沿用已有 SSH 密钥和动作服务。
- 切换会立即停止旧动作并清空缓冲，过期的会话、生成和连接检查结果不能覆盖新选择。笔记本未启动到所选系统时显示连接失败，不会自动改用另一边。这里切换的是服务连接；需要先在笔记本启动相应系统。

- 输入「向我挥挥手」「向前走」「站着」「跳舞」等并发送，默认交给笔记本实际生成。输入框不再把「挥手」等词偷偷改成本地程序动作。
- Mac 可按 Tab 聚焦输入框，⌘A 全选后输入，⌘Enter 发送；Esc 停止。最终解锁检查已验证这些键盘操作，自动化鼠标点击结果未确认。
- DiP 每次预测 40 帧（20 FPS，2 秒），客户端插值播放并提前获取下一段。新指令清除尚未播放的未来片段，在当前片段结束后接续，通常不超过 2 秒；不是逐帧立即改姿态。
- 首次启动需加载模型，实测约 5 秒。最终版本连续生成 110 段（共 220 秒动作），单段推理中位数 **58.7 ms、P95 59.0 ms**，显存实测 587 MiB；这是模型生成时间，不含传输和播放等待。此前经 SSH 转发的暖机 HTTP 请求约 71–72 ms。性能数据见 `artifacts/dip-final-inference-validation.json` 和 `artifacts/dip-http-validation.json`，不含语音或聊天链路。
- 场景设置 F6 →「DiP 连续实时」可切换「ARDY 在线片段」。这会将动作文字送给 Hugging Face 上公开的 `hugging-apps/ardy` 服务。实测新生成 4 秒片段约 9.63 秒，需等待完整结果，不是连续实时模式；公共服务可能排队或限额。没有创建付费服务或上传角色资产。
- F1–F4 为明确标注的本地程序预览；F5 为 Kimodo 官方录制样例，均不是本次模型生成。
- Esc 立即中断并回到待机。旧请求不会在停止后重新启动角色。
- F7 开关 Unity Cloth；场景设置可调风力、精度和复位衣服。
- 在角色区域普通左键或鼠标中键拖动可上下、左右移动角色构图；右键或 Shift + 左键拖动环绕，滚轮缩放。统一应用的「角色」页提供方向按钮与位置滑杆，位置和视角自动保存。
- 右上角「调整视角」或 F8 打开相机面板，可调取景高度、俯仰、环绕和距离，支持全身／半身／面部预设。F9 随时恢复默认视角。调整面板和输入框时不会同时拖动相机。微笑、视线跟随在场景设置内。
- 相机面板打开且未编辑文字时：方向键旋转，Shift + 方向键平移，+ / − 缩放，1 / 2 / 3 切换全身／半身／面部。
- 中文支持有限预设；其他动作使用英文描述。自由中文翻译、角色聊天和语音尚未接入。

## Windows 笔记本副本（E 盘）

独立目录为 `E:\YachiyoMotion`，放置当前实际使用的 **DiP + DistilBERT** 本地动作模型和专用 Python/CUDA 运行环境。模型、缓存及运行环境均位于该目录；不会开机自启。

- 双击 `启动动作服务.cmd` 加载模型并在后台运行。
- 双击 `关闭动作服务.cmd` 停止本目录的服务并释放模型显存。
- 双击 `查看动作服务状态.cmd` 查看当前状态。
- Windows 桌面的「八千代动作服务」文件夹也包含以上三个入口。
- 运行日志在 `logs`，模型校验记录在 `state/model-manifest.json`。

Windows 服务监听 `127.0.0.1:8787`，Mac 的独立通道为 `127.0.0.1:18787`；Ubuntu 通道为 `127.0.0.1:8787`。客户端 F6 中选择系统后会自动使用对应地址。高级地址仍可为两个系统分别保存；根目录 `连接Windows动作服务.command` 作为单独的连接入口保留。

Mac 切换验证见 `artifacts/backend-switch/release.json`：Windows 真实生成、切换立即停止、旧响应丢弃、切回重连和选择保存均通过。当前笔记本保持 Windows，因此 Ubuntu 的新版本实机生成留待之后验证；本轮已验证它未启动时的提示和打包连接入口。

`artifacts/windows/YachiyoMotion-Windows.zip` 是可重新配置的安装包，首次安装需要下载官方依赖和公开模型；不包含角色模型或 Windows Unity 客户端。

Windows 已验证真实挥手／走路／站立续生成、Mac 经 SSH 访问、后台进程独立于登录会话、重复启动、关闭和重新启动。测试结束后保持关闭。记录见 `artifacts/windows/windows-inference.json`、`mac-to-windows-inference.json`、`lifecycle.json` 和 `deployment.json`。

## 模型与授权状态

Kimodo/ARDY 的 LLM2Vec 编码器需要符合模型条款的 Llama 访问权限；默认 DiP 路径使用其训练时的 DistilBERT，不依赖该授权。

普通聊天 LLM 的文本回复不能代替 Kimodo/ARDY 训练时的 4096 维 LLM2Vec 特征。当前有两条真正可运行的替代路径：

1. **DiP 本地实时模型**：沿用它训练时的 DistilBERT 编码器，使用公开 checkpoint，在 RTX 3070 上保留上一段 20 帧历史持续预测下一段 40 帧。它是另一种动作模型，不能称作 Kimodo 或 ARDY 本地推理。
2. **ARDY 在线生成 API**：调用公开 `/generate` 接口，接收实际新生成的 Core 27 关节动作；恢复 API 的蒙皮逆绑定变换后映射到 Unity。该公共接口未开放历史续生成，因此只作为完整片段模式。

原 Kimodo 兼容编码器接口 `YACHIYO_ENCODER=api` 仍保留，需要真正提供相同 LLM2Vec 特征的服务；不能填写普通聊天 API 地址。原 NF4 本地编码器仍待授权，未宣称已完成。

## Unity Cloth 与角色

Unity 版本 **6000.6.4f1**，工程 `UnityProject`，场景 `Assets/Yachiyo/Studio.unity`。

- 原模型保留 9 个蒙皮网格、52 个 Humanoid 映射、原作者袖口朝向约束和辅助骨架。原始 unitypackage 不修改。
- 运行时创建 **6 片真正的 Unity Cloth 物理网格，共 1104 个顶点**：原有长裙、短前裙、左右袖子代理共 694 点，加左右下摆各 205 点。使用世界重力、风、固定接缝、拉伸/弯曲约束、连续碰撞、Unity 自动生成的虚拟碰撞粒子和自碰撞；12 个人体胶囊覆盖躯干、手臂、手掌和腿部，袖子还使用 4 个球组成的裙摆碰撞体。
- Cloth 输出位于蒙皮渲染器的根骨骼坐标中，按该坐标转换为世界位置，再传给原衣服的 48 个骨骼节点。**原先手写的重力积分已移除**。骨长保持、裙形保持和身体接触修正仍作为最后的变形约束，保留多层原网格与装饰间距。
- 袖子接缝跟随手臂位置，重置布面保持向下，避免放下手臂时把整片袖子重置到身体另一侧。骨骼传递按链长取样，并保留布面的横向朝向，避免只传递中心线造成袖面扭转。
- 上下袖片仍保留原网格的一整片上半袖；左右下摆切出独立 Cloth 后，**故意放在身体/裙子内部、关闭碰撞推挤**：正面几乎看不见，只保留布料解算的形变潜力；上层短袖和红色袖口保持原模型可见。挥手时下摆顶部仍缝在原接缝上，不会断开。
- 下摆不做裙摆接触修正、也不再用形态键压缩；缩进身体/裙摆内部是当前预期效果，优先保证正面轮廓干净。
- 独立下摆现在是 267 个渲染顶点 / 205 个物理点、20 个接缝固定点；关闭布料时恢复完整原蒙皮网格。
- 上半截衣袖的表面接触合并为一次修正，保留厚度；贴身手臂、手掌继续作为 Cloth 碰撞体，但不再用显示层修正把袖筒和袖口压扁。
- 原生求解器计算的是简化物理网格；不是对每一层褶皱和饰带逐三角形求解。极端动作、袖子与头发/饰带接触仍可能穿插，不能保证任意动作完全无穿模。
- 裙子的表面修正仍限于腰线以下；悬垂袖面另有躯干、手臂、手掌及外裙接触修正。权重标记将悬垂袖面与贴身衣领分开，头颈、脸部柔光、眼睛透明层和原生表情保持原方案。
- DiP 输出 HumanML 22 关节数据；手臂从其下垂参考姿态校准到目标 T-pose，躯干朝向从肩部恢复。四肢方向使用模型预测的关节位置校准，同时保留旋转数据中的轴向扭转，修正转身时两种输出表示之间的偏差。手指仍用角色原姿态，脚接触数据保留，但尚未加入角色脚部 IK 锁定，仍可能脚滑。
- 头发仍跟随原骨架，未整体移植 VRChat PhysBone。尚无场景物体理解、抓取、坐椅约束或语音口型。

## 笔记本服务

目录 `/home/lenovo/YachiyoMotion`，只监听 `127.0.0.1:8787`，Mac 通过 SSH 转发访问；未开放公网端口，未设为开机自启。

```bash
systemctl --user start yachiyo-motion.service
systemctl --user restart yachiyo-motion.service
systemctl --user stop yachiyo-motion.service
```

默认 `tools/start_service.sh` 使用 `.venv-ardy` 环境运行 DiP；旧 Kimodo `.venv` 和 GPT-SoVITS 环境保持独立。将 `YACHIYO_PROVIDER=kimodo` 传给启动脚本可恢复 Kimodo 服务（仍需授权或兼容远程编码器）。

模型和代码来源固定为：

- DiP 代码 `ef8edce6a53c6ab19e53b4d4dcf15bc0bc60a778`。
- CLoSD 模型仓库 `de7106b947b6f70700b5320d1cd61fef4a9ebc9b`，checkpoint `DiP_no-target_10steps_context20_predict40/model000600343.pt`，SHA256 `bcf3e482a5236a21731af571c66bd7d0274f5ddf0b6974825fa69333c77f3144`。
- DistilBERT `12040accade4e8a0f71eabdb258fecc2e7e948be`。
- ARDY 代码/骨架资产 `e706e27acc88070918308a10922b8f7174d0825e`。
- Kimodo 代码 `e1c219c99b4b94ba48be1fb6a1cbc64ecee4c954`。

公开样本 `012314.npy` 仅初始化模型历史，然后由模型生成四段站姿用于预热；用户看到的动作均为新的带文本条件推理，不播放初始化样本。不需要本地 Llama 登录。

重新准备 DiP：克隆上述固定版本到 `vendor/dip`，用 Python 3.12 环境安装官方 PyTorch `2.6.0+cu124` 和 `service/requirements-dip.txt`，运行 `tools/prepare_dip.py` 与 `tools/download_dip.py`。适配脚本只延迟未使用的 SMPL/CLIP 渲染依赖并固定 DistilBERT 版本；不改变训练权重、Transformer 或扩散计算。

## 验证和接口

```bash
.venv/bin/python -m unittest discover -s tests -v
```

测试涵盖坐标/骨架校准、分段根运动连续性、过期指令丢弃、停止中的请求和任务队列。测试中的合成 Provider 仅用于逻辑测试，不用于应用推理。

- `--verify-stream /absolute/prefix`：真实笔记本生成 → 连续挥手/走路/站立 → 换指令 → 停止及晚到响应检查。
- `--verify-ardy /absolute/prefix`：真实在线 ARDY 生成 → Unity 播放 → 停止。
- `--verify-cloth /absolute/prefix`：检查实际 Cloth 组件开关、风、转身、挥手、官方舞蹈及复位，输出截图、有限性和帧间隔。`meanTransferMs` 只测 C# 变形传递/接触修正，不包含 PhysX 求解耗时。
- `--verify-face /absolute/prefix`：正常睁眼、微笑、闭眼、头颈正面/侧面近景。
- `--verify-sleeve-final /absolute/prefix`：布料开关／动作回归，然后连续站立 60 秒，每 10 秒导出实际袖面；输出默认姿态的正面、侧面和背面截图。`tools/check_sleeve_contacts.py` 通过 Blender 检查渲染接触修正后的悬垂袖面与身体、长裙、贴身衣片的三角形相交，保留接缝处的剩余相交记录。

实时接入证据索引见 `artifacts/realtime-validation.json`；背部穿模修复见 `artifacts/sleeve-fix-validation.json`；下摆自然垂落的构建、动作回归、60 秒站立和渲染面检查见 `artifacts/sleeve-drape-validation.json`。骨骼代理未穿透不代表所有渲染三角形都无穿模，须同时看截图与限定区域的几何检查。

接口：`GET /health`；`POST /streams`；`POST /streams/{id}/step`（prompt/sequence/revision）；`DELETE /streams/{id}`；`POST /jobs`、`GET /jobs/{id}`、`GET /jobs/{id}/motion`。ARDY 完整片段接口位于 `/ardy/health` 和 `/ardy/jobs...`。每个动作来源分别排队；连续会话保留最近 4 个已接受状态，允许新指令从当前播放段分支，过期响应不能推进客户端游标。模型续生成使用最近 20 帧；姿态解码最多保留 80 帧，同时保留累计位置与朝向，持续运行不因历史变长而无限增加内存。

## 来源

- [Kimodo](https://github.com/nv-tlabs/kimodo)
- [ARDY](https://github.com/nv-tlabs/ardy) / [公共在线服务](https://huggingface.co/spaces/hugging-apps/ardy)
- [DiP 官方说明](https://github.com/GuyTevet/motion-diffusion-model/blob/main/DiP.md) / [公开 checkpoint](https://huggingface.co/guytevet/CLoSD)
- [Unity Cloth](https://docs.unity3d.com/6000.0/Documentation/Manual/class-Cloth.html)

第三方许可与角色资产边界见 `THIRD_PARTY.md`。

## 面部表情与口型

- 新增独立 `YachiyoFaceController` 组件，统一管理 `face`、`eyeline`、`eyebrow` 三个蒙皮网格的 BlendShape；`AvatarMotion` 在检测到该组件后不再直接写眨眼/微笑，避免多个脚本互相覆盖。
- 已有能力：自动眨眼（面部 + 眼线同步）、平静/开心/惊讶/难过/思考五个预设、说话时的程序化开合嘴、`vrc.v_*` viseme 接口、可选视线目标；所有切换带短过渡。
- 界面左下新增「表情」按钮：平静、开心、惊讶、难过、思考、说话 F11、眨眼 F10。
- 对话/语音后续只需调用 `SetEmotion(...)`、`SetTalking(...)`、`SetViseme(...)`、`SetLookTarget(...)`；口型时间序列可直接写入 viseme。
- `--verify-face <prefix>` 会输出 open / smile / blink / surprised / sad / thinking / talk / side / neck 等面部截图；日志中的 `FACE_CONTROLLER` 行会列出实际找到的网格数、形态数和管理数量。


## 台式机 AMD 副本（2026-11-13）

台式机部署根目录为 `D:\YachiyoMotion`。桌面「八千代动作 - 启动／关闭／状态」指向根目录脚本；DiP 与 StreamTalk 共享一个 Python 进程。无开机自启。当前 Windows ROCm 环境从已有工作环境复制到本目录，后续依赖安装只发生在副本内；原 GPT-SoVITS 不受启动／停止脚本管理。

Mac 选择「台式机 Windows · AMD」后通过独立 SSH 通道 `127.0.0.1:28787` 连接。`tools/connect_desktop.sh` 支持 `YACHIYO_DESKTOP_HOST` 和 `YACHIYO_DESKTOP_PORT` 覆盖；高级自定义 HTTP 地址仅用于原生 DiP 客户端，导演语音动作代理固定到本机 28787。笔记本 Windows（18787）和 Ubuntu（8787）继续可选。Ubuntu 实机生成仍留待笔记本切换系统后验收。

恢复安装需准备 `service/`、`windows/`、`vendor/dip`、`vendor/streamtalk` 及合法模型资源，运行 `windows/install_amd.ps1 -BasePython <可运行的Windows-ROCm-python.exe>`。该脚本拒绝覆盖没有 AMD 所有权记录的既有 runtime。`tools/prepare_dip.py` 会为 DiP 上游包补充明确包边界，避免被环境中无关的 `utils.py` 截获。
