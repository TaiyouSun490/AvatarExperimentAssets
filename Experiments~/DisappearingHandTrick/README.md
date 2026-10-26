# Disappearing Hand Trick 研究プロジェクト — FPS Arms / PCVR

> `codex/tendon-vibration-dht` ブランチには、腱振動・運動錯覚版の `TendonHandVR` シーンを追加しています。
> **腱振動版を使う場合は [専用の導入・操作手順](Documentation/TendonHand.md) を参照してください。**
> 以下は保持している元の DHT シーンの手順です。
> 分岐の目的と関連研究は [回復測定の研究メモ](../../Documentation~/TendonVibrationRecovery.md) を参照してください。
> この分岐を取得する場合は、下記 clone コマンドのブランチを `codex/tendon-vibration-dht` に変更してください。

AvatarExperimentAssets の FPS Arm モジュールを使った、Windows PCVR 用の Disappearing Hand Trick 試作です。
Unity **6000.5.9f1**、URP **17.5.0**、OpenXR **1.17.1**、XR Hands **1.8.1**。

AvatarExperimentAssets の **Diappearing ブランチ**に、今回の研究実装をプロジェクト一式として保存しています。
共通パッケージ自体の Runtime / Editor に実験機能を切り出したものではありません。

## GitHub からの取得

```powershell
git clone --branch Diappearing https://github.com/TaiyouSun490/AvatarExperimentAssets.git
cd AvatarExperimentAssets/Experiments~/DisappearingHandTrick
```

共通パッケージは同じリポジトリのルートを `file:../../..` で参照します。submodule は不要です。
第三者製 FPSHand モデルは含まれないため、使用権のあるローカル素材を次の手順で導入してから Unity を開いてください。

```powershell
./Tools/Import-FPSArm.ps1 -Source 'C:/path/to/FPSHand'
./Tools/Build.ps1
```

## 起動

- `Start-PCVR.cmd`：HMD を接続し、使用する OpenXR ランタイムを有効にしてから起動。
- `Start-DesktopPreview.cmd`：HMD なしの操作・描画確認。ログには simulation と記録。
- Unity Hub では `Experiments~/DisappearingHandTrick` フォルダーを追加し、`Assets/Scenes/DisappearingHandTrickVR.unity` を開く。

実行ファイル：`Builds/Windows/DisappearingHandTrick.exe`。
Quest 単体 APK ではなく Windows PCVR ビルドです。SteamVR / Meta Link 等の OpenXR ランタイムで実行します。
素手追跡は使用する HMD・PC 接続方式・ランタイムが XR Hands の関節情報を提供する場合に使えます。
コントローラーでも動作しますが、触覚を含む錯覚の確認には机上の素手条件を使ってください。

## 実験の流れ

1. 座位で実際の机に両手を置き、正面を向いて **F / ALIGN TABLE**。仮想机上面を両手首の平均位置より 4 cm 下に合わせます。手の厚さに応じて Inspector の机位置を調整できます。
2. 両手を肩幅程度に離し、**SPACE / START / 左 X** で開始。
3. **5 秒の基準表示 → 40 秒の位置ずれ → 3 秒保持**。見えている手首をリング上に保つように動かします。
4. 両手の描画がそれぞれ内側へ最大 **12 cm** ずれます。見た目をリング上に保つと、実際の手が外側へ移動します。
5. 既定では右手を隠し、リングも消します。右手を静止させ、左手で右手があると感じる場所を探します。探索中も左右の表示オフセットを維持します。
6. **SPACE / 右 A / 右ピンチ 1 秒** で探索終了。未操作なら 15 秒で終了し、両手が再表示され、2 秒かけて位置ずれがゼロになります。

描画の位置だけを変更します。XR 追跡座標、腕の IK ターゲット、ログの実座標は変更しません。
手首から約 10 cm より近位側の腕をシェーダーで切り、FPSArm の手・短い前腕を表示します。
消失中の手から影や深度シルエットが残らない構成です。
実物の机との接触は利用者が行い、接触の自動検出・力覚出力はありません。

## 操作

| 操作 | キー / VR |
|---|---|
| 机を合わせる | F / ALIGN TABLE / 未調整時に左ピンチ 1 秒 |
| 開始 | SPACE / START / 左 X / 左ピンチ 1 秒 |
| 探索を終了 | SPACE / CONFIRM SEARCH / 右 A / 右ピンチ 1 秒 |
| 即座に中止・表示を戻す | R / Esc / RESET / 右 B |
| VR メニュー開閉 | F8 / 左 Menu |
| 消す手の左右切替 | T / LEFT・RIGHT |
| 位置ずれなしの対照条件 | B / DRIFT・CONTROL |
| 保持を待たず消す | H（操作者用。早期移行としてログ記録） |
| VR メニュー選択 | 右コントローラーの向き＋トリガー／素手では視線＋右ピンチ |
| プレビューの手を動かす | 矢印：右手、Shift＋矢印：左手、P：探索動作 |

開始時に条件を固定し、試行中は変更しません。位置ずれ量は 0–20 cm/手、速度はずれ時間で設定します。
細かな各段階の秒数は `DisappearingHandExperiment > Settings` で変更できます。
FPS Arms 側の長さ校正ジェスチャーは、実験操作との競合を避けるため無効にしています。
腕長・コントローラーの手首オフセットはシーンの Rig / Arm コンポーネントで調整できます。

追跡喪失、手／コントローラー入力の切替、追跡原点 Transform の移動、ランタイムからのリセンター通知、0.5 秒を超えるフレーム停止、VR アプリのフォーカス喪失で試行を中止します。
再開には机を合わせ直してください。追跡できない手は非表示にし、回復後は位置ずれなしで表示します。

## ログ

`%USERPROFILE%/AppData/LocalLow/AmemiyaLab/DisappearingHandTrick/Trials/`

試行ごとの CSV。UTC 開始時刻と条件、段階遷移、最大 30 Hz の左右実手首座標・表示手首座標、探索側指先、追跡状態、入力種別を記録します。
コントローラー条件の探索点は手首／grip の代理点で、指先ではありません。座標系は Unity world、単位は m。
表示座標は消失中も算出される「表示すればこの位置」の値です。可視性列と合わせて解釈してください。
ログが作成できない場合は開始を拒否し、途中の書き込み失敗は操作者画面に表示します。
接触検出や錯覚の主観評定は実装していません。主観体験の強さは別途評価が必要です。

## 依存素材と再ビルド

- AvatarExperimentAssets：このプロジェクトの二つ上のフォルダーにあるリポジトリルートを UPM で参照。MIT。
- `Assets/ThirdParty/FPSHand`：使用権のあるローカル FPSHand を導入。FBX・テクスチャ・meta は Git には含まれません。
- FinalIK、全身アバター、Meta Movement はこの FPSArm 版では使用しません。
- Unity 公式依存は `Packages/manifest.json` と `packages-lock.json` で管理。

第三者モデルはこの PC での利用用で、Git の追跡対象から除外しています。モデルを含むフォルダーやビルドの配布条件は元アセットのライセンスに従ってください。
出所とリビジョンは `Documentation/ThirdParty.md` を参照。

```powershell
# シーン生成、検証、Windows ビルド
./Tools/Build.ps1
# 保存済みシーンの検証のみ
./Tools/Build.ps1 -ValidateOnly
```

別の PC に移す場合は上記の `Diappearing` ブランチを取得し、使用権のある FPSHand を `Tools/Import-FPSArm.ps1 -Source <フォルダー>` で導入します。

## 実装・検証範囲

- `TrialProtocol.cs`：時間・段階・描画ずれ。対照条件、左右対象、早期消失、中止と復帰。
- `DisappearingHandExperiment.cs`：追跡、操作、机合わせ、表示更新、ログ接続。
- `ShiftedHand.shader`：単眼／両眼共通の世界座標変位と可視性制御。Single Pass Instanced 対応。
- `ExperimentBuild.cs`：シーン生成、OpenXR 設定、Windows ビルド。
- `PcvrSession.cs`：PCVR 起動時に OpenXR セッションを開始。デスクトッププレビューではセッションを開始しません（Unity の起動前ネイティブ機器検出ログは出る場合があります）。
- `ExperimentValidation.cs`：状態遷移・実座標保持・素材・XR 設定・描画の検証。
- `PlayerSmokeCheck.cs`：`--desktop --smoke-test` 指定時のみ実行するビルド済みプレイヤーの一連動作確認。

検証結果は `Logs/validation.txt` と `Builds/Smoke/result.txt`、プレビュー画像は `Documentation/Previews/`。
デスクトップ確認は HMD での立体視、追跡精度、遅延、実際の錯覚成立を保証しません。

## 参考

[Newport & Gilpin (2011), Multisensory disintegration and the disappearing hand trick](https://pubmed.ncbi.nlm.nih.gov/21996499/)
および [原作者による 2012 年の説明](https://illusionoftheyear.com/2012/05/the-disappearing-hand-trick/) を基にした VR 向け試作です。
原実験の映像装置を厳密に再現したものではなく、秒数・12 cm の設定値は変更可能な実装既定値です。
