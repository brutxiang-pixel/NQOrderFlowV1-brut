# GEX：EM / 代理链如何进入策略快照（Gate0→Gate1 数据合同）

现状：策略只消费本地 `OPFStrategyV1_gex_snapshot.json`（及派生 levels CSV）。Gate0 已为 EM、ProxyChain、覆盖率等留空列；**无数据源前禁止编造**。

## 1. 目标字段（写入快照 JSON）

建议挂在 `symbols.NQ`（MNQ 执行图仍读 NQ 结构位）：

```json
{
  "symbols": {
    "NQ": {
      "levels": {
        "callWall": 0,
        "putWall": 0,
        "zeroGamma": 0
      },
      "proxyChain": "NQ",
      "proxyLabeledOnStatus": true,
      "wallWeightConvention": "OI",
      "expiryWindow": "NearWeek",
      "coverageStatus": "Ok",
      "dataRealtime": true,
      "expectedMove": {
        "fullPoints": null,
        "remainingUpper": null,
        "remainingLower": null,
        "usedPct": null
      },
      "statusLine": ""
    }
  },
  "status": "Ready",
  "updatedAt": "2026-10-10T01:00:00Z",
  "dataDate": "2026-10-10"
}
```

F0 最小通过：`status=Ready` + CW/PW/ZG +（推荐）`proxyChain=NQ` 且覆盖非 UntrustedWall。

## 2. 推荐摄入路径（按易用排序）

### A. NeomancerX 面板 → 手工/半自动导出（会员已有）

1. ATAS 上挂 NeomancerX GEX；确认状态行出现 **NQ 代理**（MNQ 自有链极薄，2-02）。
2. 盘前固定抄录 / 截图后填表：三关键位、EM 全日与剩余、已耗%、口径、到期窗、覆盖率警告。
3. 写入 `%APPDATA%\ATAS\StrategyConfigs\` 旁或约定路径的 `OPFStrategyV1_gex_snapshot.json`。
4. 策略启动或刷新调度读取；SHA 变化才重写 levels CSV（与现 Gate0 一致）。

适合 Gate0–Gate1 Shadow：一天一次盘前快照即可支撑空间门与体制标签。

### B. 现有 DeltaPEX / 其它 enrich（若你已有管线）

1. 在 enrich 输出中增加与上表同名的字段（或映射表）。
2. 合并进 `GexDailySnapshot` 反序列化模型；未知字段保持可空。
3. **不要**把 NeomancerX 账密写进仓库或快照。

### C. 指标 DLL 直连（中期）

若 NeomancerX 提供可读 API/共享文件：由独立小工具轮询 → 写快照 JSON → 策略只读文件。保持「策略不嵌会员站爬虫」。

## 3. EM 用法（写入后）

- 盘中门禁用 **剩余** 期望边，不是开盘全日边（3-11 / 5-04）。
- `usedPct > 70` → 追价类影子标签（SZ Short / OC 确认）。
- 结构止损相对剩余边 R:R &lt; 1 → `SHADOW_NO_SPACE` / 日后 `SKIP_GEX_EM_RR`（需预注册样本）。

## 4. 代理链校验

| 状态行/字段 | Gate0 行为 |
|---|---|
| Proxy=NQ、覆盖 Ok | Prefilter 可激活 |
| MNQ 自有薄链 / 无代理标签 | `SHADOW_PREFILTER_INACTIVE` |
| 墙位不可信 | 同上，不发明墙距标签 |

## 5. 本周最小落地

1. 定一份「盘前 JSON 模板」+ 你每天填 8 个数字（CW/PW/ZG/EM满/EM上/EM下/已耗%/代理是否 NQ）。
2. Replay 用手工快照验证审计列非空。
3. 稳定后再接 DeltaPEX 或自动导出。

账号密码：**禁止**入库；仅本机 ATAS/导出工具登录。
