# 腱振動＋仮想手の運動錯覚版

`codex/tendon-vibration-dht` の `TendonHandVR.unity`。Windows PCVR / Unity 6000.5.9f1 / FPSArm。
実手を静止させ、腱振動と仮想手の角度変化で誘導し、振動を止めた後の感じる角度を記録するための試作です。

## 導入と起動

```powershell
git clone --branch codex/tendon-vibration-dht https://github.com/TaiyouSun490/AvatarExperimentAssets.git
git clone https://github.com/TaiyouSun490/ESP32HapticController.git
git -C ESP32HapticController checkout 794fa2acb4d3572eeddad752907c4ed5a9ec4f9b
cd AvatarExperimentAssets/Experiments~/DisappearingHandTrick
./Tools/Import-FPSArm.ps1 -Source 'C:/path/to/FPSHand'
./Tools/Import-ESP32.ps1 -Repository 'C:/path/to/ESP32HapticController'
./Tools/Create-TendonWorkspace.ps1 -Destination 'C:/UnityProjects/TendonHand'
cd C:/UnityProjects/TendonHand
./Tools/Build-Tendon.ps1
./Start-Tendon-Desktop.cmd
```

Unity 6000.5 では、共通パッケージ内に入れ子になったプロジェクトを直接開くとスクリプトアセットの読み込みが失敗しました。
`Create-TendonWorkspace.ps1` はリポジトリ外に独立した作業コピーを作成し、共通パッケージ参照を元のリポジトリへ設定します。
既存の移動先は上書きしません。Library / ログ / ビルドはコピーしません。
**Unity Hub には作成した作業コピーを登録**し、`Assets/Scenes/TendonHandVR.unity` を開いてください。
作業コピーで編集したソースは、コミット前にリポジトリ側へ反映してください。リポジトリを動かした場合は manifest のローカル参照も更新します。

`Start-Tendon-PCVR.cmd` は HMD 用、`Start-Tendon-Desktop.cmd` は固定した手による表示・操作確認用です。
実行ファイルは作業コピーの `Builds/TendonWindows/TendonHandTrick.exe`。
デスクトップモードは常に模擬出力で、実機へ接続しません。PCVR でも起動時の既定は模擬出力です。
元の DHT は別シーン `DisappearingHandTrickVR.unity` と `Tools/Build.ps1` に保持しています。

## 試行

1. HMD と OpenXR の手／コントローラー追跡を用意し、支持した両手を静止させます。**F / ALIGN** で机と正面方向を合わせます。
2. PC の操作者パネルで対象手、回復時の表示、誘導角度・時間、振動の有無を設定します。
3. 模擬出力では **C / CONNECT**。実機では `Simulated output` を外し、COM ポートとチャンネルを指定して接続します。接続時は全出力をミュートし、状態応答を待ちます。
4. **SPACE / START** で、基準表示 → 誘導 → 停止命令の書き込み待ち → 回復観察 → 再表示・終了へ進みます。
5. 回復観察では対象手を動かさず、感じる角度を口頭で回答します。操作者が `Record felt deg` または Enter で記録します。回答用の対象手ピンチや探索は要求しません。

既定値は基準 5 秒、誘導 15 秒、観察 60 秒、回転 −25°、80 Hz、正規化振幅 0.1、チャンネル 0。
これらは試作用設定で、錯覚成立や物理的振幅を保証する値ではありません。振幅は 0–1 の指令値で mm ではありません。
周波数・振幅・チャンネルと誘導中の視覚運動は操作者が変更できます。開始時に試行条件を固定します。

| 停止後条件 | 観察中の対象手 |
|---|---|
| HoldDisplaced | 誘導終了時の位置・姿勢で静止して表示 |
| Hidden | 対象の仮想手を非表示 |
| Veridical | 追跡した実手の位置・姿勢に即座に一致させて表示 |

観察中にゼロへ戻す自動アニメーションはありません。観察終了／中止時に通常表示へ戻ります。
誘導中の振動と仮想手運動は独立したチェックボックスで、両方あり・視覚のみ・振動のみ・両方なしを設定できます。
条件のランダム化や参加者管理は未実装で、試行ごとに操作者が選びます。

`Forearm` は手首から正面方向と逆へ指定半径だけ離した幾何学的支点の周りで手を回転させます。
支点は追跡した肘ではありません。机の右方向が回転軸で、正の角度は既定の正面姿勢で手を下げ、負は上げます。
`Wrist` は手首位置を支点に手だけを回転させます。使用する腱・支持姿勢・錯覚方向に合わせて選び、視覚軌道を確認してください。
描画のみを変え、元の追跡値・IK ターゲットは変更しません。FPSArm の手と短い前腕を表示します。

| 操作 | キー |
|---|---|
| 机合わせ / 接続 / 開始 | F / C / Space |
| 誘導を早期終了し回復観察へ | H |
| 中止し表示を復元、再調整 | Esc / R |
| HMD メニュー表示切替 | F8 |
| 入力した口頭角度報告を保存 | Enter |

PC パネルはスクロールできます。HMD のメニューは右コントローラーのレイ＋トリガー、または視線＋右ピンチで選択できます。
試行中の設定変更・再接続は不可です。通常は操作者が PC から進めます。

## ログと時間基準

`%USERPROFILE%/AppData/LocalLow/AmemiyaLab/TendonHandTrick/TendonTrials/` に試行ごとの CSV。
設定、UTC、PC 単調増加時計、基準姿勢・支点・軸、段階遷移、最大 30 Hz の実手と表示手の位置・姿勢、可視性、追跡種別、主観角度を保存します。
口頭回答は `reported_angle` に操作者の入力時刻で記録します。発話開始時刻ではありません。
0、2、5、10、20、30、60 秒の測定候補は PC パネルと `report_cue` に出ます。終了時刻と重なる最後の cue は終了遷移により出ない場合があります。
自動的な音声提示・音声認識、知覚の回復判定、所有感評定はありません。

- `start_requested` / `stop_requested`：メインスレッドが出力を要求した時刻。
- `start_write_complete` / `stop_write_complete`：シリアルワーカーで PC の書き込みが戻った時刻。コマンド ID で対応付けます。
- `recovery_visual_applied`：停止書き込みを受け、メインスレッドが表示条件を適用した時刻。HMD の実際の発光時刻ではありません。
- `phase_s`・姿勢・出力状態はイベントをメインスレッドで処理した際のスナップショットです。ワーカー発イベントの時刻は `pc_monotonic_s` を使います。行の到着順が時刻順とは限りません。
- `visual_angle_deg` は誘導で達した操作角で、Veridical で追跡に戻った姿勢は `visual_*` 列に記録します。主観回答と混同しないでください。

ESP32 のバイナリ命令には個別 ACK がなく、状態問い合わせを通信の生存確認に使います。
**書き込み完了は装置の適用完了や物理的振動停止の測定ではありません。** 実振動の立ち上がり・減衰・HMD 遅延を扱う測定には別途センサーと時刻較正が必要です。

## 出力と中止処理

ESP32HapticController の Unity 1.0.2 Runtime 3ファイルをローカルに導入し、921600 baud のバイナリ通信で正弦波を送ります。
選択した1チャンネルのみを解除し、残りはミュートを保ちます。停止は全チャンネル・アンプのミュートです。
したがって実験中はこの装置を専有し、別アプリから同時に操作しないでください。

追跡喪失、入力種別変更、リセンター／原点移動、0.5秒超のフレーム停止、VR のフォーカス喪失、対象手の基準位置からの移動（既定5cm／20°）、通信・ログの異常で中止します。
独立ワーカーはホスト更新の0.75秒途絶、誘導時間超過、2秒の状態応答途絶でもミュートを試みます。
USB 切断・プロセス強制終了・機器故障では PC から停止を保証できません。参照したファームウェアには通信断時の自動停止がないため、実機の独立した停止手段が必要です。

## 検証

```powershell
./Tools/Build-Tendon.ps1 -ValidateOnly
./Builds/TendonWindows/TendonHandTrick.exe --desktop --smoke-test
```

2026-10-26: Windows ビルド成功。腱振動版178項目、元の DHT の80項目と FPSArm の484姿勢が PASS。
`Logs/tendon-validation.txt` は状態遷移・表示条件・出力ワーカーの検証結果です。
`Builds/TendonSmoke/result.txt` は実行ファイルで左右×3回復条件、振動なし対照、中止、CSV と描画を確認した結果です。
同日のプレイヤーテストも PASS（終了コード0）。右手の3条件の画像を目視でも確認しました。
同フォルダーに回復条件ごとの PNG を保存します。これらは模擬出力の検証であり、実際の腱振動・HMD 内の知覚・残効の成立を確認したものではありません。
