# Avatar Experiment Assets

`codex/dht-look-away` に、通常 DHT の **HMD 視野外変更版** を追加しました。
両手が描画視野外に出た間に1回ずつ位置を変え、見直すまで次の変更を待ちます。
[導入・操作・原著との違い](Experiments~/DisappearingHandTrick/Documentation/LookAwayDHT.md) を参照してください。
通常版と腱振動版も別シーンで保持しています。

## 腱振動・運動錯覚版の研究ブランチ

`codex/tendon-vibration-dht` は `Diappearing` の `9ffbce3` から分岐した研究用ブランチです。
Disappearing Hand Trick の位置ずれを、腱振動刺激とバーチャルハンドの提示によって作り、
振動を停止した後に手の位置・姿勢感覚がどのように回復するかを調べることを目的にしています。
元の `Diappearing` ブランチは保持しています。

FPSArm と [ESP32HapticController](https://github.com/TaiyouSun490/ESP32HapticController) を使う
腱振動版を `TendonHandVR` シーンとして追加しました。刺激停止後は、ずれた仮想手の保持・非表示・
実手に一致した表示を比較し、実手の追跡値・表示値・口頭回答の角度を別々に CSV 記録します。
導入・起動は [腱振動版の手順](Experiments~/DisappearingHandTrick/Documentation/TendonHand.md) を参照してください。
知覚の回復を自動判定するものではなく、実機での振動・錯覚成立は別途確認が必要です。
関連研究、測定指標、実装時の変更点は [腱振動停止後の回復に関する研究メモ](Documentation~/TendonVibrationRecovery.md) を参照してください。

Unity 6 / Windows PCVR 用のアバター実験共通パッケージです。
LiquidBodyNVidia の独自実装からトラッキング・キャリブレーション部分を切り出しました。
**第三者モデル、FinalIKソース、Meta SDKサンプル、学習済みモデルは同梱しません。**

## 研究プロジェクト: Disappearing Hand Trick

`Diappearing` ブランチには、FPSArm を使った Disappearing Hand Trick の Unity 研究プロジェクトを
[Experiments~/DisappearingHandTrick](Experiments~/DisappearingHandTrick/README.md) に保存しています。
両手の表示位置を徐々にずらす、片手を隠す、反対の手で探索する、表示を戻す、という試行と
対照条件・左右切替・CSV 記録を含みます。共通ライブラリ化はせず、今回の実験実装を一式保持しています。

```powershell
git clone --branch Diappearing https://github.com/TaiyouSun490/AvatarExperimentAssets.git
cd AvatarExperimentAssets/Experiments~/DisappearingHandTrick
./Tools/Import-FPSArm.ps1 -Source 'C:/path/to/FPSHand'
./Tools/Build.ps1
```

Unity Hub では `Experiments~/DisappearingHandTrick` をプロジェクトとして開きます。
このプロジェクトは同じリポジトリのルートにある共通パッケージを相対パスで参照します。
`Experiments~` は UPM の通常インポート対象外なので、パッケージ利用先へ実験シーンやコードを自動追加しません。
モデル・ビルド・Library・実験ログは含みません。操作と検証範囲はプロジェクトの README を参照してください。

## 導入

### 一人称の腕だけを使う実験

`AvatarExperiments.FpsArms`に、MeltingHandIllusionから切り出したFPS Armモジュールを追加しました。
全身Humanoid／FinalIKなしで、素手・コントローラー入力、指追跡、左一回の両腕校正、
腕と手の拡縮、肘ポール補正、ねじれ分散を利用できます。
モデルを同梱せず、利用者のモデルをProfileから設定します。
**Tools > Avatar Experiments > FPS Arms**が導入メニューです。
手順・モデル要件・検証範囲は[Documentation~/FPSArms.md](Documentation~/FPSArms.md)を参照してください。

確認対象: Unity **6000.5.9f1**、URP **17.5.0**、XR Hands **1.8.1**、OpenXR **1.17.1**。
Unity 6 の全バージョンに対する互換性は未確認です。

Package Manager の「Install package from git URL」で次を指定:

```text
https://github.com/TaiyouSun490/AvatarExperimentAssets.git
```

本リポジトリはUnityプロジェクトではなく **UPMパッケージ** です。
URPプロジェクトへ導入してください。Unity公式パッケージはpackage.jsonで参照し、
ソースを複製しません。

1. 使用権のあるHumanoidアバターを自分でインポートし、シーンへ配置。
2. 全身IKと腕脚長補正を使う場合は **FinalIKを別途購入・インポート**。
   無くてもコンパイルできますが、頭・手のターゲット生成だけでは身体は追従しません。
3. Project Settings > XR Plug-in ManagementでStandaloneのOpenXRを有効化。
   Hand Trackingと使用するコントローラープロファイルを有効化し、Project Validationを確認。
4. Floor原点、スケール1のTracking Originと、その子のXRカメラを用意。
   本パッケージのXrSessionが指定カメラの姿勢を更新します。
   同じカメラを更新するTracked Pose Driver等は併用しないでください。
   Player SettingsのActive Input HandlingはBothに設定（F8ショートカット用）。
5. アバターを選択し **Tools > Avatar Experiments > Create setup for selected Humanoid**。
   生成されたオブジェクトのTrackingOriginとXrCameraを割り当てる。
6. アバターのルートはTracking Originの子にせず、直立状態、正しい床位置から開始。
   Play前にHMDをPCへ接続。SteamVR / Meta Linkなどランタイム設定は導入先で選択。

Meta Movement、FleX、CUDA、液体シミュレーションへの依存はありません。
ただしXR Handsの実際の追跡可否はHMD・接続経路・OpenXRランタイムに依存します。

## VRメニュー

- 右 **A / B**、左Menu、F8で開閉・視野外メニューの再配置。
- 裸手では左の **親指と中指を約0.8秒合わせる** と開きます。人差し指は離します。
- 選択は **手のレイ + 同じ手の親指と人差し指のピンチ**、またはコントローラーレイとトリガー。
  裸手の関節データが有効なら裸手を優先し、コントローラーの認識が残っていてもピンチを受け付けます。
  手首から中指の付け根へ向かう向きで狙います。ピンチ開始時は直前の照準を保持します。
  メニューを開いた後・追跡復帰後・入力切替後は、一度ピンチを離してから選択します。
- Body: 立位のHMD高さでサイズ調整。Floor設定と静止が必要。
- Lengths: 左右の腕（肩→肘→手首）、脚（股関節→膝→足首）を±1cm調整。
  身長スケールを合わせてから使います。基準比率70–130%。脚長の自動推定はしません。
- Wrists: 左右の手首角度。LiquidBodyNVidiaの基準を共通デフォルトとして、
  左 `(10, 65, 95)`、右 `(180, 100, 80)` 度。追跡手首の回転へローカル補正を掛けます。
  Reset wristsもこの値に戻ります。既存の保存値は引き続き優先します。
- Fingers: Calibrateをピンチで選び、すぐピンチを離します。3秒の準備中に
  両手の掌をHMDへ見せ、指を自然に伸ばして広げ、親指も掌から離します。
  約1秒静止すると登録します。左右それぞれの追跡待ちを表示し、追跡ロストで基準を作り直しません。
- Save on this PC: 現在値を保存、次回Playで自動読込。前の保存を1世代保持。
- Restore previous save: 前の身体寸法・手首設定をプレビュー復元。確定はSave。
  指は独立保存で、この操作では戻しません。

保存先は各プロジェクトのPlayerPrefs、キーはアバター名に基づきます。
身体寸法や指の個人別保存値は配布していません。手首の上記基準値は共通コードに含みます。
同名の異なるアバターはキーが衝突するため、Avatarアセット名を固有にしてください。

### コントローラーによる3点測定

Lengths > Measure joints with controller で対象の腕・脚を選択。
左側の身体は右コントローラー、右側は左コントローラーで測ります。
緑の球が測定点（コントローラーのgrip原点。レーザー先端ではありません）。
JointPointCalibration.ProbeOffsetで物理的な接触点とのずれを設定できます。

腕は肩→肘→手首、脚は股関節→膝→足首へ順番に球を合わせ、
各点で約0.3秒静止して、トリガーを離してから押します。
対象の腕・脚は3点の測定が終わるまで動かさないでください。
登録中は計測に使う手のUI選択を停止します。反対の手からCancelを選択でき、
A/Bでメニューを閉じることでも測定を中止します。
90秒のタイムアウト、追跡ロスト時の再押下ガード、原点移動時の中止、
距離の妥当性検査があります。

3点が揃っても自動反映せず、Apply measurement でプレビューして鏡で確認。
Save on this PC で次回用に保存します。失敗・中止では以前の寸法を保持します。
表面に当てた位置と関節中心は一致しないため、医学的な身体計測ではありません。
上腕・前腕等の比率は元のアバターを保ち、測定した2区間の合計長を適用します。
姿勢が動いたことを身体トラッカーなしで完全に検出はできません。

### 全身鏡

Tools > Avatar Experiments > Create full-height mirror でシーンに配置して保存。
独自のURPシェーダーとQuadのみを使用し、外部モデルや素材は不要です。
ローカル-Z側が鏡面。既定サイズ1.5×2.1m、前方2m、解像度は最大1024。
左右眼を別々に反射描画し、再帰描画を防止します。
レイヤー31を鏡専用に使用するため、導入先で競合しないようにしてください。
反射は眼ごとに追加描画が発生します。重い場合はResolutionScaleを下げてください。

## 実験への接続

- AvatarRig: 頭・両手のターゲットとHumanoidへの参照。
- AvatarTrackingInput: XR Handsの手首を優先、未追跡時はコントローラーへフォールバック。
- FPS腕のAutomatic入力も裸手を優先し、描画直前のコントローラー更新による指姿勢の上書きを防ぎます。
- FinalIkBridge: リフレクション経由の任意依存。FinalIK本体は配布しません。
  `ConfigureExternalSolver` で実験側が更新する既存IKへ接続でき、VRIKを追加しません。
- AvatarCalibration / XRHandHumanoidFingerDriverのConfigureは任意のprofileIdを受け取り、
  同じモデルを使う複数の実験の保存値を分離できます。
- AvatarCalibration.TrackedEyePose: 実験で使用する目線の追跡姿勢を供給できます。
  座標はスケール1のTracking Originに対するローカル値です。
- XRHandHumanoidFingerDriver.ManualUpdate / ApplyPose: 実験側のIK直後などに指更新を配置できます。
- VrCalibrationMenu.OpenChanged / ConsumeInput: 校正中の実験停止・入力の取り合い防止に使います。
- AvatarCalibration.CanResizeOverride: 実験側で寸法変更を禁止するガード。
- AvatarCalibration.DimensionsChanged: メッシュ等のキャッシュ再構築通知。
- AvatarTrackingInput.UiOwnsGestures: VR UIが入力を使用中かを実験側で参照。
- FirstPersonHeadVisibility: 任意ON。頭部別RendererとCC系の頭部マテリアル名を
  判定する方式で、全アバターの混在サブメッシュを自動解析するものではありません。
  自分のモデルと鏡で確認してから有効にしてください。

液体、RPM推定サーバー、ハプティック、LAN同期は本パッケージに含みません。
旧シーンの参照切れを避けるため、Kevin使用シーンもコピーしていません。

## ライセンス / 検証

独自コードは既存のMIT LICENSEに従います。除外範囲は[PROVENANCE.md](PROVENANCE.md)。
2026-09-26: 空の検証用Unity 6000.5.9f1プロジェクトへlocal UPMとして導入し、
FinalIK・Meta SDK・元プロジェクトなしでEditorコンパイル成功を確認しました。
Tools > Avatar Experiments > Run math smoke tests で座標・測定・反射計算を検証できます。
v0.2.0では15項目PASS。元プロジェクトのPlayでもUI遷移、合成3点の適用・中止、
既存ミラーの反射描画カウンターを確認。実際の身体を測定する精度とHMD両眼の見え方は未確認です。
レイ座標変換、不正値拒否、指の基準回転・直線判定、UIシェーダー同梱を検証しています。
v0.4.0（2026-10-08）: 導入先のKevin全身デモで左右26関節の入力から指の描画・
ピンチ検出・描画直前の姿勢維持、540ケースの指回転、手首基準、校正UI、
身長調整・保存復元・既存IKとの接続を模擬入力で検証し、Windowsビルド成功。
入力修正後の実機での指追従・ピンチ選択の成否はまだ確認できていません。
実機HMD、各自のアバター、別途導入したFinalIKを含む組み合わせの動作確認は必要です。
