# Marble Hand Illusion — VR派生

FPSArmの右手を使い、**仮想ハンマーの接触・短い振動・石の打撃音・大理石の見た目**を組み合わせる実験です。
`DisappearingHandTrick` から独立したUnityプロジェクトです。手の位置をずらす処理や腱振動による運動誘導はありません。

Unity **6000.5.9f1** / URP **17.5.0** / Windows PCVR / OpenXR。
使用権のあるFPSHandモデルと、ESP32使用時のランタイムはローカル導入し、Gitには同梱しません。

## 導入・起動

リポジトリの `Experiments~` 内を直接Unityで開かず、作業用プロジェクトを外に作ります。

```powershell
git clone --branch codex/marble-hand-illusion https://github.com/TaiyouSun490/AvatarExperimentAssets.git
cd AvatarExperimentAssets/Experiments~/MarbleHandIllusion
./Tools/Create-MarbleWorkspace.ps1 -Destination 'C:/Experiments/MarbleHandIllusion'
cd C:/Experiments/MarbleHandIllusion
./Tools/Import-FPSArm.ps1 -Source 'C:/path/to/FPSHand'
./Tools/Import-ESP32.ps1 -Repository 'C:/path/to/ESP32HapticController'
./Tools/Build-Marble.ps1
```

ESP32を使わない場合、そのインポートは省略できます。共通FPSArmパッケージは元のリポジトリを参照します。
`Start-Marble-PCVR.cmd` はHMD、`Start-Marble-Desktop.cmd` はデスクトッププレビューで起動します。
デスクトップは必ずSimulationとなり、物理振動機器へ接続しません。
Unity Editorでは `Tools > Marble Hand > Create experiment scene` でシーンを生成できます。
第三者アセットを導入した作業用コピーを編集した場合、意図したソース変更をリポジトリに戻して保存してください。

## 実施

PC画面の操作パネルを実験者が操作します。参加者のHMD内には手・ハンマー・短い指示を提示します。

1. 実際の机に右手を置き、`ALIGN TABLE` で仮想机の高さを合わせる。
2. 音・視覚・振動のON/OFF、条件・時間・出力を選ぶ。初期値は **3つともON / 5分 / Simulation**。時間ボタンで30秒デモに切替可能。
3. 振動ONでESP32を使う場合はCOMポート・0始まりのチャンネル・周波数を設定し `CONNECT OUTPUT`。XRコントローラーも選択可能。振動OFFなら出力の接続は不要。
4. `START TRIAL` → 参加者の事前回答を記録 → `Save baseline & begin`。
5. 右手を机に置いたまま接触箇所を見る。接触間隔は0.8〜1.6秒、30接触までに石の音と大理石表示が増える。
6. 刺激終了後に事後回答を記録し `Save post ratings & finish`。記録完了で肌の表示に戻る。

`ESC` / `STOP / RESET` / 右コントローラーBで中止。`F8`でPC操作パネルを開閉します。
手・頭の追跡喪失、120 ms超のフレーム停止、出力エラーでも中止します。事後回答中は刺激が停止し、その条件の見た目を維持します。
中止と完了で肌の表示へ戻ります。実際の手を叩く装置・接触センサーは接続しておらず、刺激は仮想的な接触時刻に合わせて提示します。

音・視覚・振動はそれぞれ独立に切り替え、8通りを選べます。視覚OFFでは**手は肌のまま表示**し、ハンマーと大理石化を非表示にします。
音OFFでは音を再生せず、振動OFFでは出力装置へパルスを送信しません。設定は試行開始時に固定され、JSONに保存されます。
振動をOFFにすると出力接続は閉じます。再びONにした場合は `CONNECT OUTPUT` で接続し直してください。

| 条件 | 振動 | 石の音 | 右手の見た目 |
|---|---|---|---|
| SynchronousMarble | 接触時 | 接触時 | 徐々に大理石 |
| DelayedSound | 接触時 | 800〜1600 ms遅延 | 徐々に大理石 |
| UnchangedSkin | 接触時 | 接触時 | 肌のまま |

同じseedなら接触時刻・音の選択・音量ランプを条件間で一致させます。これは3条件の試作で、完全な要因計画や無作為割付ではありません。
上記条件はON/OFFに重ねて適用されます。例えばUnchangedSkinでは視覚ONでも大理石化せず、音OFFでは遅延条件の音は出ません。

## 記録・仕様

`%USERPROFILE%/AppData/LocalLow/AmemiyaLab/MarbleHandIllusion/MarbleLogs/` に試行ごとのCSVと設定JSONを保存します。
試行開始時の設定は固定されます。CSVには条件に対応する設定ファイル、接触番号、DSP予定時刻、描画フレーム時刻、
振動の送信完了時刻、右手の追跡姿勢、大理石化の程度、事前・事後の回答、中止理由を記録します。
時刻は予定値・ソフトウェア観測値で、実際の振動・音の物理的な立ち上がりを測った値ではありません。

ESP32の初期パルスは **160 Hz / 振幅0.1 / 50 ms**。これは今回の実装値であり原著の値ではありません。
使用する振動子と接触方法に合わせて調整してください。XRコントローラーの振動周波数はランタイム依存です。
ESP32出力は独立スレッドで有限パルス・予約取消・ハートビート停止を処理します。
接続時・停止時の `MuteAll` は装置全チャンネルに作用するため、本実験専用の装置として使います。
USB断線やプロセス強制終了時の物理停止はホストコードだけでは保証できません。ファームウェア側の停止機能は別途確認が必要です。

原著との違い、音源、検証範囲は [実験設計メモ](Documentation/Protocol.md) を参照してください。
