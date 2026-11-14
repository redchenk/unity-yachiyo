八千代 Windows 一键启动包（Windows 10/11 x64）

1. 将整个文件夹解压到本地目录，不要直接在压缩包中运行。
2. 双击「启动八千代.exe」。角色、直播界面和本机动作服务自动启动。
3. 直接关闭八千代窗口即可退出。这次启动器启动的动作服务也会关闭；
   启动前就已运行的动作服务和 GPT-SoVITS 保持原状。

本包包含 Unity 3D 客户端、直播导演、Node 运行时和 WebView2 界面宿主。
无需另外安装 Unity、Node 或手工打开导演进程。
动作模型复用电脑中已安装的 YachiyoMotion，依次检查 D/E/F/C 盘。
本包不附带大型模型权重、账号令牌、私有聊天记录或他人的设置。
语音仍连接已有 GPT-SoVITS 服务。首次在这台电脑使用时，在界面设置
中配置自己的对话模型和语音地址；本机语音地址通常为 http://127.0.0.1:9880。

启动设置.json（用记事本编辑，保留 JSON 格式）：
motionRoot：留空自动查找。手动指定示例 "E:\\YachiyoMotion"。
motionUrl：本机动作服务默认 http://127.0.0.1:8787。
backend：台式机选 desktop，RTX 3070 笔记本选 windows。
startMotion：true 自动启动；false 只连接已有服务。
stopOwnedMotion：true 随客户端退出；false 保留本次启动的动作服务。

角色区域：左键拖动平移，右键或 Shift+左键拖动旋转，滚轮缩放。
也可以使用界面的视角控制。位置和服务配置会保存在当前 Windows 用户下。

故障排查：
- 界面无法打开：安装微软官方 Microsoft Edge WebView2 Runtime：
  https://developer.microsoft.com/microsoft-edge/webview2/
- 动作服务无法加载：检查所选 YachiyoMotion 的 logs 文件夹。
- 启动器日志：%LOCALAPPDATA%\YachiyoUnityDirector\launcher.log
- 导演日志：%LOCALAPPDATA%\YachiyoUnityDirector\unified.log
- Unity 日志：%USERPROFILE%\AppData\LocalLow\Tsukuyomi\Yachiyo Motion Studio\Player.log

本包包含你的角色资源，供你自己的电脑使用，请勿作为公开仓库附件上传。
