# URUI 本机 Agent API v1

## 连接、线程与 Job

在 Unity 菜单启用 Local API。读取项目 `Library/URUI/Authoring/endpoint.json`：`url`、`token`、`editorSession`、`project`、`apiVersion`。服务仅监听 127.0.0.1，重载/重启后重新发现端点；默认不开启。macOS/Linux 连接文件限制为当前用户读写。

每个请求携带：

```text
Authorization: Bearer <token>
X-URUI-Session: <editorSession>
Content-Type: application/json
```

`POST /v1/commands` 的 body 是 JSON 对象，返回 HTTP 202 与 `{job,status,editorSession}`。用 `GET /v1/jobs/<id>` 查询，状态为 queued/running/done/failed/cancelled；完成时 `result` 是操作结果对象。`DELETE /v1/jobs/<id>` 只取消尚未开始的任务，正在执行或完成时返回 409。不要把收到 202 当成编辑或渲染成功。

Unity 操作在主线程执行。请求最大 1 MiB，排队和保留任务均有界；过旧完成任务会被淘汰。JSON 拒绝重复键、无效数字、尾随内容及不完整 Unicode。重编译会终止服务并取消旧任务。浏览器 Origin、Host、token 和会话均受检查。

## 操作与参数

除 capabilities/documents.list 外明确提供 `mode: "authoring" | "live"`。写操作还需要目标会话 `session` 和整数 `expectedRevision`，分别从最新响应获取；该 session 是 authoring/live 会话，不是端点中的 editorSession。

| op | 参数与行为 |
| --- | --- |
| `capabilities` | 返回协议、会话、属性 schema、可用扩展和操作列表 |
| `documents.list` | 列出当前加载页面的 Unity id、名称、来源 |
| `document.open` | authoring；`path`，打开 HTML 工作副本并尝试预览 |
| `document.select` | live；`document` 为 documents.list 返回的整数 id |
| `document.read` | authoring；可选 `path`；返回 text、dirty、fileRevision，源码范围单位为 UTF-16 |
| `document.edit` | authoring；`path` 可省略为主文件，`text` 为候选全文；校验后发布工作副本 |
| `document.reload` | authoring；`discardLocal` 默认 false，明确放弃本地修改才设置 true |
| `document.save` | authoring；显式保存当前工作集，检查磁盘冲突 |
| `hierarchy.children` | 可选 `target`，`offset` 默认 0，`limit` 默认 100、最大 500；live 根请求返回 root |
| `hierarchy.find` | `name` 精确匹配 id，返回至多 500 项；允许重复 id，编辑使用返回的身份 |
| `selection.set` | `target`，在 URUI Inspector 显示节点并清空 Unity 默认 Inspector |
| `properties.read` | `target`，可选字符串数组 `properties`；区分 value、computed、origin |
| `properties.set` | `target,property,value`，或 `edits:[{target,property,value}]`；删除声明用 `unset:true` |
| `structure.insert` | authoring；`parent,html,index`，index=-1 追加 |
| `structure.remove` | authoring；`target` |
| `structure.move` | authoring；`target,parent,index`，拒绝循环 |
| `style.rule` | authoring；`path,ruleStart,property,value/unset`；ruleStart 是当前 CSS 规则的 UTF-16 起点 |
| `history.undo` / `history.redo` | 当前模式会话的历史，不执行任意业务对象的 Undo |
| `preview.viewport` | authoring；`width,height`，每边 64..8192 |
| `preview.render` | 刷新当前版本；作者预览在 Play 让出 Surface |
| `preview.capture` | 实际渲染截图到 Library，返回绝对 path 和结果版本 |
| `preview.input` | `kind:pointer` 搭配 `phase:Down/Move/Up`、数字 x/y、button/pressedButtons；或 `kind:key,key,pressed`；或 `kind:text,value`；`kind:reset` 清理输入 |
| `extension` | `extension:"effect", operation, args`，同时提供 target |
| `persistence.apply` | live；`target,properties:[...]`，明确应用选中改动到可靠来源 |

属性名称使用 `style.width`、`style.opacity`、`attr.id`、`attr.src`、`text`、`value`、`image` 等。具体可用项读取 capabilities，勿假设浏览器全部 CSS 或任意 C# 私有属性可编辑。image 接收 Texture2D 项目资产路径。value 默认不发送业务 ChangeEvent。

节点 id 在作者源码版本/结构内或 Play 会话内有效，禁止缓存到下一次页面加载。源码修改与结构操作可能使旧节点 key 失效。所有写入必须使用最新版本。结构事务一次一个操作，属性批量一次校验后发布；失败不会留下半套候选源码。

响应区分 `sourceRevision`、`liveRevision`、`renderedRevision`、`frameId`。sourceRevision 是工作集版本，liveRevision 是工具调参版本，不表示游戏全部业务数据的版本。截图的路径和 frameId 属于该次实际捕获。作者预览无效时可读取诊断和最后有效渲染版本。

## Effect 扩展

没有安装 Effect 时返回 effect_not_installed。`args` 是简单键值对象：

| operation | args |
| --- | --- |
| `asset.open` | `path`，EffectPreset、Material 或粒子 Prefab |
| `asset.read` | 读取工作副本状态与 assetRevision |
| `asset.setParticle` | `assetRevision,property,value`；常用模块见下 |
| `asset.setMaterial` | `assetRevision,property,value`；使用 Shader 已定义属性，颜色/向量可用逗号分隔数值，纹理使用项目路径 |
| `asset.setPreset` | `assetRevision,property,value`；EffectParameters 的公开标量、枚举、Color、向量、EffectRange 和纹理字段；遵守 Range |
| `asset.apply` | `assetRevision`，显式应用到原资产 |
| `asset.saveAs` | `assetRevision,path`，新 Assets 路径，不能覆盖已有资产 |
| `asset.discard` | `assetRevision`，放弃副本并恢复实例绑定 |
| `particles.play/pause/restart/refresh` | 目标为有效 urui-particles 节点 |
| `particles.seek` | `time` 秒 |
| `particles.step` | `delta` 秒 |

常用粒子参数：duration、loop、startLifetime、startSpeed、startSize、maxParticles、emissionRate、shapeEnabled、shapeRadius、startColor。复杂曲线/渐变和更多模块通过原生 Inspector 编辑。粒子源、材质和模拟空间仍受 Effect 支持范围限制。

## 标准库客户端

在 Unity 项目根运行。示例修改工作副本并截图，最后一步 save 单独列出以明确持久化。

```python
import json, time, urllib.request

with open("Library/URUI/Authoring/endpoint.json", encoding="utf-8") as f:
    endpoint = json.load(f)

def command(**body):
    headers = {"Content-Type": "application/json",
               "Authorization": "Bearer " + endpoint["token"],
               "X-URUI-Session": endpoint["editorSession"]}
    req = urllib.request.Request(endpoint["url"] + "v1/commands",
        data=json.dumps(body, ensure_ascii=False).encode("utf-8"), headers=headers)
    with urllib.request.urlopen(req, timeout=15) as response:
        job = json.load(response)["job"]
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline:
        req = urllib.request.Request(endpoint["url"] + "v1/jobs/" + job, headers=headers)
        with urllib.request.urlopen(req, timeout=15) as response:
            state = json.load(response)
        if state["status"] == "done":
            return state["result"]
        if state["status"] in ("failed", "cancelled"):
            raise RuntimeError(state)
        time.sleep(.05)
    raise TimeoutError(job)

caps = command(op="capabilities")
opened = command(op="document.open", mode="authoring", session=caps["session"],
    expectedRevision=caps["sourceRevision"], path="Assets/MyUI/Inventory.html")
found = command(op="hierarchy.find", mode="authoring", name="page-title")
target = found["data"]["nodes"][0]["id"]
edited = command(op="properties.set", mode="authoring", session=found["session"],
    expectedRevision=found["sourceRevision"], target=target, property="text", value="Equipment")
capture = command(op="preview.capture", mode="authoring")
print(capture["renderedRevision"], capture["frameId"], capture["data"]["path"])
# 显式保存：
# command(op="document.save", mode="authoring", session=edited["session"],
#         expectedRevision=edited["sourceRevision"])
```

直接修改 HTML/CSS 也被支持。编辑器只能检测外部写入，不能约束绕过接口的 Agent；外部改文件后重新读取版本并检查冲突。

## 动画命令

使用相同的 mode/session/expectedRevision 规则。`workspace.mode` 用 `workspace:"layout"|"animation"|"live"` 切换与人工相同的工作区，响应 `workspaceMode` 显示当前选择。动画工作区未录制的 properties.set 只临时预览，布局工作区才修改基础属性；显式 animation.key 始终编辑关键帧。`animation.read` 为只读，其余动画操作包含预览状态变化，也需要会话与最新版本。源码写操作仅 authoring；Play 内 sample/play/stop 需要 mode=live，Edit 内需要 mode=authoring，混用会拒绝。切换活动 Document 先释放旧预览，不能继续修改旧页面。

| op | 参数 |
| --- | --- |
| `animation.read` | 返回 path、definition、time、playing、recording、selectedTrack |
| `animation.open` | path，明确的 `.urui-sequence.json` |
| `animation.create` | 创建当前页面同名空序列，仅工作副本 |
| `animation.preset` | target 为当前源码节点 key；preset、start(默认0)、duration(默认.3)，返回 selectedTrack |
| `animation.particles` | target 为粒子节点 key；source、start(默认0)、duration(默认1) |
| `animation.track` | track 为轨道 id，changes 对象；支持 start/duration/iterations/alternate/muted/easing/target/keyframes/source/seed/speed |
| `animation.lock` | track、locked；锁定轨道需先解锁才能修改 |
| `animation.remove` | track；移除轨道，保留 CSS 定义 |
| `animation.key` | track、position(0..1)、property(CSS名)、value；或 keys 数组包含这些字段，整体事务；unset:true 移除属性 |
| `animation.keys.move` | keys:[{track,position}]、seconds 为秒数差、copy 默认false；整体事务 |
| `animation.keys.remove` | keys:[{track,position}]，整体事务 |
| `animation.sample` | time 秒，定位并暂停，不触发自然完成事件 |
| `animation.play/pause/stop` | 共用时间轴预览所有者 |
| `animation.record` | enabled；开始时还需 track、target，之后 properties.set 写当前关键帧 |

`animation.track.changes.target` 是运行时根内唯一 HTML id，和命令顶层 target 的源码节点 key 含义不同。键帧 CSS 文件、JSON 和 HTML 的最终保存仍用 `document.save`。录制只接受当前绑定单节点的可动画样式；变更选择/源码/模式会结束录制。

使用上文 Python command 客户端：

```python
state = command(op="capabilities")
mode = "authoring"
session = state["session"]

def write(op, **args):
    global state
    state = command(op=op, mode=mode, session=session,
                    expectedRevision=state["sourceRevision"], **args)
    return state

write("workspace.mode", workspace="layout")
write("document.open", path="Assets/UI/Reward.html")
found = command(op="hierarchy.find", mode=mode, name="reward")
target = found["data"]["nodes"][0]["id"]
write("animation.preset", target=target, preset="popup", duration=0.3)
track = state["data"]["selectedTrack"]
write("animation.key", track=track, position=0.5, property="opacity", value="0.7")
write("animation.sample", time=0.15)
shot = command(op="preview.capture", mode=mode)
# 审查 shot["data"]["path"]，再明确保存：
write("document.save")
```

每步读取 job 的 done/failed 与 sourceRevision/renderedRevision/frameId。旧会话、过期版本、锁定轨道、冲突位置或非法关键帧批量均拒绝，失败不发布部分源码。缺少 Effect 时不删除对应轨道或关键帧，播放给出扩展诊断。

`document.save` 保存源工作集，data.pendingAssets 列出仍未应用的资产；它不会默默 Apply。资产继续使用 effect 扩展的 asset.apply 与独立 assetRevision。切换缓存页面也会更新 sourceRevision，旧页面的节点 key 与写凭证不得重用。
