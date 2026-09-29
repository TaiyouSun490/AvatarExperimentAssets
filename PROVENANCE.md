# 配布物と除外物

## 独自コード

LiquidBodyNVidia の Assets/LiquidAvatar の独自実装を元に再構成:
VRメニュー、身体寸法・手首設定、XR Hands指リターゲット、FinalIK接続、
XRカメラ、頭部の一人称非表示、URPワールド空間UIシェーダー。
鏡は独自のURP反射コード・シェーダーとUnityのQuadで構成し、
元プロジェクトのフレーム形状・プレハブ・マテリアルはコピーしていません。
AvatarRig / AvatarTrackingInput / AvatarExperimentSetup とEditor導入補助は
第三者モデルや液体機能に依存しないようこのパッケージ用に整理しました。

## 同梱しないもの

- Kevin / Character Creatorモデル・テクスチャ・マテリアル
- FinalIK / RootMotionソース・DLL・デモ
- Meta MovementSDKサンプル、Meta SDKソース
- Unity XR Handsサンプル / HandVisualizer
- NVIDIA FleX / CUDAネイティブDLL
- RPM等の外部研究コード、学習済みモデル
- ESP32コントローラー外部リポジトリ、OSS_metaverse
- サードパーティー参照を含む元シーン・プレハブ
- 個人の身体寸法・キャリブレーション値、録画、ログ、認証情報

Unity公式依存はUPMの依存宣言のみです。FinalIKはユーザー自身が取得してください。
このパッケージのMITライセンスは、別途取得した依存物のライセンスを変更しません。
# FPS Arm module

Runtime/FpsArms and Editor/FpsArms contain project-authored tracking, calibration,
IK and twist-distribution code extracted from the MeltingHandIllusion FPS-arm work.
They use the repository's MIT license. No third-party model, material, texture,
animation clip, paid IK implementation or participant calibration is included.
The fp_male_hand profile helper records paths/references to a user's separately
imported model; it does not distribute that model or its animations.
