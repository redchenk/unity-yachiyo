# Unity Yachiyo

把 [Yachiyo Live2D Studio](https://github.com/redchenk/yachiyo-live2d-studio) 的直播导演、对话、弹幕、流式语音和演唱系统接到 Unity 八千代角色。

日常只打开 **Yachiyo Motion Studio.app**（安装到 `~/Applications/`）。界面沿用原导演台：左侧导航、中间角色、右侧控制栏、底部字幕。中间角色由 Unity 渲染，本地导演服务随主应用自动启动和关闭，不需要另外打开 Electron 或终端。

当前统一应用的打包目标是 **macOS Apple Silicon**。Windows／Ubuntu 继续作为可切换的 GPU 动作服务端；本次没有制作 Windows 统一客户端。

## 已接入

- 保留原导演台的观众队列、人格、记忆、LLM 控制、字幕、TTS 和 RVC 演唱流程。
- Unity 接收语义动作、情绪、眨眼、视线及原始 A/I/U/E/O 口型，不经过 Cubism 参数转换。
- 实际音频开始播放时才启动表演；口型跟随音频时钟。暂停、拖动进度、结束和取消关闭旧口型。
- 身体动作默认走现有模型服务；「角色」页可以明确选择本地预览。挥手、鞠躬、转身、走路、鼓掌、跳舞可请求模型；点头、轻微摇头、歪头是本地微动作。
- 回执包括 accepted / started / completed / cancelled / unsupported / failed。模型失败会显示失败，不会悄悄改播预览。
- session、epoch、commandId、seq 和 utteranceId 隔离过期命令。取消、切换服务、断线重连不补播旧动作。
- 隐藏、锁定、重置作用于真实 3D 场景。角色区域可拖动旋转、Shift + 拖动平移、滚轮缩放。
- Windows／Ubuntu 选择位于右侧「角色」页，沿用现有 SSH 隧道与服务配置。
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
