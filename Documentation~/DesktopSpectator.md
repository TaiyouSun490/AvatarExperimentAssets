# デスクトップ観客表示

Unity 6000.5 / URP 17.5 のWindows PCVR用。通常のゲームウィンドウを観客へ見せます。
HMDは既存の一人称のままです。2画面は同じウィンドウ内の左右分割を意味し、
物理的な2台のモニターへの別出力ではありません。

## 導入

- `AvatarExperimentSetup.EnableDesktopSpectator`をONにするか、
  **Tools > Avatar Experiments > Create desktop spectator**で追加します。
- `ParticipantCamera`へ既存のXRカメラを割り当てます。カメラを新しい追跡元へ置き換える必要はありません。
- `Subjects`へ構図に含めたいRendererを設定します。2人の実験なら両方のアバターを含めます。
- `DetailTarget`は接触部など、拡大する中心です。実行時に`DetailPointProvider`でワールド座標を返すこともできます。
- 自分の頭を非表示にする`FirstPersonHeadVisibility`がある場合は`HeadVisibility`へ割り当てます。
  一人称小窓も頭の内側を描画しなくなり、第三者視点には頭が表示されます。
- `SpectatorMask`は第三者カメラだけのレイヤーマスクです。浮遊メニューや鏡を観客側で除外できます。

```csharp
var spectator = gameObject.AddComponent<AvatarExperiments.DesktopSpectator>();
spectator.Configure(xrCamera, bothAvatarRenderers, headVisibility);
spectator.DetailPointProvider = () => currentContactPoint;
spectator.SetLayout(AvatarExperiments.SpectatorLayout.PictureInPicture);
```

## 操作

デスクトップのゲームウィンドウにフォーカスを置いて操作します。

| 操作 | 内容 |
| --- | --- |
| F2 / 画面上のボタン | 第三者のみ → 左右分割 → 右下に一人称ワイプ |
| F3 / ボタン | 全体と接触部の拡大を切替 |
| 右ドラッグ | 中心の周囲を回り込む |
| 中ドラッグ | 構図を上下左右へ移動 |
| ホイール | ズーム |
| Home / Reset | 構図を初期化 |
| F4 | 操作案内・ラベルを隠す／戻す |

マウス操作は第三者視点の領域内だけに作用します。ワイプや右側の一人称映像はHMD姿勢に追従します。
全体表示は指定RendererのBoundsに合わせます。分割やウィンドウの縦横比変更でも全体を収めます。
F3の拡大先は実験側で供給する一点です。身体の部位による接触判定の区分は導入しません。

## 描画と負荷

追加カメラは`stereoTargetEye=None`と`allowXRRendering=false`で、XRカメラのミラー出力より後に描きます。
HMDカメラの姿勢・投影・viewport・targetTexture、XRミラー設定は変更しません。
追加カメラにはAudioListenerを付けません。無効化・シーン終了時に追加カメラを破棄します。
同時に有効な観客出力は1つです。加算シーンで重複した場合は最初の有効なコンポーネントが担当します。

一人称小窓はHMD中央の姿勢から再描画する単眼映像で、既定の画角は75度です。
HMDの片眼画像そのもののコピーではありません。ステレオ固有の効果やXR専用描画とは差があり得ます。
`ParticipantFieldOfView`で画角を調整できます。

第三者のみでは追加1回、分割／ワイプでは追加2回のシーン描画が必要です。
第三者カメラはポスト処理・MSAAなし。重いデモではまず第三者のみを使い、
`SpectatorMask`で不要な描画を除外してください。
`OutputDepth`は他の画面用カメラより大きくします（XRカメラよりは自動的に後へ調整）。
この実装はURP向けです。HDRP、モバイルHMD単体、複数ディスプレイへの別々の出力は対象外です。

## 検証

**Tools > Avatar Experiments > Run desktop spectator smoke tests**で3レイアウト×3縦横比、
全体の収まり、XR無効設定、追跡カメラ設定の保持、不要ビュー停止、破棄・再有効化を検証します。
実機HMDでの最終的な見え方とフレーム時間は導入先のシーン・機器で確認してください。
