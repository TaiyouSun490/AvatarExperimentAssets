# Avatar Experiment Assets

Unity 6 / Windows PCVR 用のアバター実験共通パッケージです。
LiquidBodyNVidia の独自実装からトラッキング・キャリブレーション部分を切り出しました。
**第三者モデル、FinalIKソース、Meta SDKサンプル、学習済みモデルは同梱しません。**

## 導入

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
- 選択は **コントローラーレイ + 同じ手のトリガー**。視線ポインターではありません。
- Body: 立位のHMD高さでサイズ調整。Floor設定と静止が必要。
- Lengths: 左右の腕（肩→肘→手首）、脚（股関節→膝→足首）を±1cm調整。
  身長スケールを合わせてから使います。基準比率70–130%。脚長の自動推定はしません。
- Wrists: 左右の手首角度。
- Fingers: 開いた両手の姿勢を明示的に登録。追跡ロストで基準を作り直しません。
- Save on this PC: 現在値を保存、次回Playで自動読込。前の保存を1世代保持。
- Restore previous save: 前の身体寸法・手首設定をプレビュー復元。確定はSave。
  指は独立保存で、この操作では戻しません。

保存先は各プロジェクトのPlayerPrefs、キーはアバター名に基づきます。
元プロジェクトのユーザー個人の値は配布していません。
同名の異なるアバターはキーが衝突するため、Avatarアセット名を固有にしてください。

**コントローラーで肩・肘・手首等の点を採取する測定機能は未実装**です。
現在は身長自動フィット + 腕脚長の手動フィットです。

## 実験への接続

- AvatarRig: 頭・両手のターゲットとHumanoidへの参照。
- AvatarTrackingInput: XR Handsの手首を優先、未追跡時はコントローラーへフォールバック。
- FinalIkBridge: リフレクション経由の任意依存。FinalIK本体は配布しません。
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
Tools > Avatar Experiments > Run math smoke tests の8項目もバッチ実行でPASS。
レイ座標変換、不正値拒否、指の基準回転・直線判定、UIシェーダー同梱を検証しています。
実機HMD、各自のアバター、別途導入したFinalIKを含む組み合わせの動作確認は必要です。
