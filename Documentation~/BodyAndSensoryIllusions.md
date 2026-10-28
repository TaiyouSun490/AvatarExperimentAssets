# 視覚運動同期を中心としない身体・感覚錯覚の候補

自分の動きとアバターの動きを同期させることを主な誘導手続きとしない例を集めた。
身体所有感が関係する研究も含む。視覚と触覚など、異なる刺激同士の同期を使う手続きは対象に含む。
原著が使った刺激と、右列のVR実装案は区別する。
錯覚が生じるか、どの程度続くかは条件・個人によって異なる。

| 錯覚と原著・研究例 | 変化する知覚／原著の刺激 | VRへの実装案・必要なもの |
|---|---|---|
| [Slime Hand Illusion — Kodaka et al. 2022](https://journals.sagepub.com/doi/10.1177/20416695221137731) | 鏡の中でスライムがつままれて伸びるのを見ながら、隠れた実手の皮膚もつままれると、手の皮膚や指が伸びるように感じる。 | スライム状の手と、実際の皮膚へのつまみ・引っ張りを対応させる。単なる振動への置換は原著の再現ではない。 |
| [Anne Boleyn Illusion — Newport et al. 2016](https://journals.sagepub.com/doi/10.1177/2041669516669732) | 鏡越しの指と机の上をなぞる視覚と、隠れた手の指への触覚の対応を変え、6本目の指があるように感じさせる。 | VRでなぞる位置を操作し、実指への接触と合わせる。触覚位置を精密に提示できる仕組みが必要。 |
| [Finger stretching — Perera et al. 2017](https://link.springer.com/article/10.1007/s00221-017-4935-2) | 実指を軽く引く／押す触覚と、指が伸びる／縮む視覚を組み合わせる。研究では刺激後の指の大きさ判断も測定。 | 指の視覚変形と外部からの引っ張り・押しを対応させ、刺激後の知覚を記録する。 |
| [Magnetic Touch Illusion — Guterstam et al. 2016](https://www.sciencedirect.com/science/article/pii/S0010027716301512) | ゴム手には触れず、近くの空間を筆でなぞる視覚と隠れた実手への筆の接触を組み合わせ、筆と手の間に磁力のような感覚を生じさせる。 | 仮想手に触れないブラシと実手への触覚を対応させる。VR版での成立は未検証。 |
| [Pinocchio illusion — Lackner 1988](https://pubmed.ncbi.nlm.nih.gov/3378137/) | 自分の鼻に触れながら上腕の腱を振動すると、鼻が伸びるように感じる場合がある。身体形状・運動感覚の錯覚。 | 腱振動に加え、鼻や手の見た目を変えて知覚を比較。実際の接触と振動子の装着が必要。 |
| [Waist-shrinking illusion — Ehrsson et al. 2005](https://journals.plos.org/plosbiology/article?id=10.1371/journal.pbio.0030412) | 手を腰に当てて手首周辺の腱を振動し、腰が縮むように感じる。身体サイズの錯覚。 | 腰に当てた手と胴体の見た目を変えて比較。腱振動と身体への接触が必要。 |
| [Cutaneous rabbit — Geldard & Sherrick 1972](https://pubmed.ncbi.nlm.nih.gov/5076909/) | 離れた皮膚位置を続けて刺激すると、中間点も触れられたように感じる。触覚位置の錯覚。 | 腕の複数の振動子を時間差で制御し、VR上に触覚点の視覚を重ねる。複数の刺激位置が必要。 |
| [Parchment-skin illusion — Jousmäki & Hari 1998](https://pubmed.ncbi.nlm.nih.gov/9512426/); [Guest et al. 2002](https://www.psy.ox.ac.uk/publication/16996) | 手をこする音の周波数成分を変えると、手の乾き・粗さの感じ方が変わる。聴触覚の錯覚。 | 手をこする動作・音を取得し、音をリアルタイム加工。音声の入力、遅延と音量の管理が必要。 |
| [Size–weight illusion — 研究例](https://pubmed.ncbi.nlm.nih.gov/22912704/) | 同じ重さでも、小さい物体のほうが大きい物体より重く感じられやすい。重さの錯覚。 | 同一の実物を持ちながらVR上の見かけの大きさを変える。現実の重さを与える物体・対応付けが必要。 |
| [Vection — VRでの研究例](https://pubmed.ncbi.nlm.nih.gov/10603425/) | 周囲の視覚風景が動くと、静止していても自分が動いているように感じる。自己運動の錯覚。 | 静止した観察者の周囲に動く風景を提示し、運動感の強さ・開始時刻を比較。 |
| [Thermal grill illusion — Bouhassira et al. 2005](https://www.sciencedirect.com/science/article/pii/S0304395904005846) | 隣り合う温・冷刺激によって、灼熱感や痛みを感じる場合がある。温痛覚の錯覚。 | 温冷を個別に制御する専用装置が必要。現在の振動子だけでは再現できない。 |

Slime Hand、Anne Boleyn、Finger stretching、Magnetic Touchはいずれも、本人の運動追従よりも、
視覚と外部からの触覚の対応操作が中心となる。実装案は研究上の候補であり、この文書が錯覚の成立や新規性を保証するものではない。
