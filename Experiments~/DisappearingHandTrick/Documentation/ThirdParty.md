# ローカル依存素材

| 依存 | 出所 | このプロジェクトでの利用 |
|---|---|---|
| AvatarExperimentAssets 0.4.0 | このリポジトリのルート | UPM から `file:../../..` で参照。基準 commit `a4eb9f740cd30f20390b2338f90ffa70409ca4ae`。Runtime / Editor はそのまま利用。ライセンスはルートの LICENSE（MIT）。 |
| fp_male_hand.fbx | 既存 `../NightmareHandIllusion/Assets/ImportedModels/FPSHand` | ユーザーがダウンロード済みのローカルモデル。左右腕・指の骨格とポーズ。 |
| fp_male_hand_color_var_01_D.tga | 同上 | 肌テクスチャ。 |
| fp_male_hand_N.tga | 同上 | 元素材とともに保持。今回の軽量シェーダーでは未使用。 |
| Unity URP / XR Hands / OpenXR / XR Management / Input System / UGUI | Unity Package Manager | バージョンは manifest / lock file。 |
| ESP32HapticController Unity 1.0.2 | https://github.com/TaiyouSun490/ESP32HapticController commit `794fa2acb4d3572eeddad752907c4ed5a9ec4f9b` | `unity/ESP32-Vibration-Controller-1.0.2.unitypackage` の Runtime 3ファイルを `Tools/Import-ESP32.ps1` でローカル導入。ソース・パッケージはこの Git に再配布しない。 |

ESP32 パッケージの SHA-256: `46C7A35DDD513396DE99EF8CEE789CCB844E9554DA9303D9F6BB82A0F4EFE636`。
対応ファームウェアは上記リビジョンの `firmware/sample_tpa3130D2_v3_binary`。書き込みは利用者が行う。

第三者製モデルとテクスチャ、その `.meta` は出所を保持してコピーしています。素材の所有権・再配布権を本プロジェクトのコードライセンスへ変更するものではありません。
FPSHand とビルド出力を Git ignore し、GitHub にはソース・設定・独自生成シーンと文書のみを保存します。

XR 設定の初期アセットは既存の `FusionBodyIllusion` のローカル設定を基にし、このプロジェクトのビルダーで PCVR 用の機能を設定し直しています。
実験コード、シェーダー、シーン生成コードは本プロジェクト用に作成しています。
