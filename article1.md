# 炎の描画まで

## 基本の仕組み

みんなが想像するきれいなオレンジ色の炎や黒い煙は、実は割と単純な仕組みです。
煙の粒子は温度によって色が変わります。熱くなると色が赤→黄色→白へと変化し、光も強くなります。

粒子1個分なら簡単に理解できますが、空間に粒子がいっぱいある場合はどうでしょうか。
密度と温度が一定であれば、これも簡単ですね。
密度は透明度に対応します。密度が高いほど、煙が濃くなります。

それなら、空間を小さなキューブ（ボクセル）に分けて、1ボクセル内の煙の密度と温度を一定とし、
すべてのボクセルを重ねて描画すればいいわけです。

これは「ボリュームテクスチャ」と言います。3次元のテクスチャのことですね。

残る問題は以下の2つです。
1. ポリゴンがないものをどうやってGPUで描画するのか？
2. どうやってボリュームテクスチャを生成するのか？

1つずつ見ていきましょう。

### レイマーチング

普通の描画だとオブジェクトの表面の情報しか必要ありませんが、今回はオブジェクトの中の情報も必要になります。

解決方法は割と簡単です。フラグメントシェーダーでカメラからピクセルに向かって線（レイ）を飛ばし、その線に沿ってオブジェクトの中を少しずつ（一歩ずつ）進んでいきます。

いろいろと最適化はできますが、基本はそれだけです。歩幅（ステップサイズ）と回数を決めて、1歩ずつ進みながらボリュームテクスチャのデータをサンプリング（取得）します。
そして、取得したすべてのデータを重ね合わせて描画すれば終わりです。

### 流体シミュレーション

ここからが今回のメインディッシュです。
煙と熱のエミッター（発生源）を用意し、そこから流体がどう動くのかを物理的にシミュレーションします。

普通のゲームならここまでする必要はないのですが、やることでいろんなメリットがあります。
一番重要なのは、炎がプレイヤーの動きからちゃんと影響を受けられることです。
プレイヤーのアクションによって炎が煽られて強くなったり、動きに合わせてなびいたりできます。
最終的には、環境にあるオブジェクトと相互作用することも可能です。木製のものを加熱したり、燃料を爆発させたりといったこともできるようになります。

## 流体シミュレーションのシェーダー

数学の部分が怖いので最後に記載しましたが、本当は先に読むべきです。まあ読まなくても分れるように頑張りますが。

流体シミュレーションはボクセルごと処理がまったく同じので全部非同期で処理行えます。
これがGPUにピッタリのタスクのでコンピュートシェーダーで行います。
使ったことがないなら怖そう言葉ですが、描画しないシェーダーって意味だけです。

基本的に以下の処理を行います：

1. 外部の力の影響を計算する
2. 速度の発散を計算する
3. 発散情報を利用して圧力を計算する(数回反復する）
4. 圧力の影響を速度に加算する
5. 移流の処理を行う

#### 外部の力
これはとても簡単です、みんなが学んだニュートン方式と同じです。
基本的に必要のは浮力と重力です。
浮力が温度にが高いほどに強い、重力が密度が高いほど強い

```hlsl
// Buoyancy
float buoyancyForce = (heat * Buoyancy) - (smokeDensity * SmokeWeight);
vel.y += buoyancyForce * DeltaTime;
```

ここの `Buoyancy` と `SmokeWeight` がシェーダー外で設定できるパラメータだけです。
物理的に性格な値が必要はありません。

#### 速度の発散
圧力の計算に必要です。圧力のループの外に置く形が効率いいので別のステップに分けています。
微分を計算しますが、これは周りの値の差だけで計算しています

```hlsl
uint3 pos = groupThreadId + uint3(1, 1, 1); // Offset for the padding

float3 vL = velCache[GetCacheIndex(uint3(pos + int3(-1,  0,  0)))];
float3 vR = velCache[GetCacheIndex(uint3(pos + int3( 1,  0,  0)))];
float3 vD = velCache[GetCacheIndex(uint3(pos + int3( 0, -1,  0)))];
float3 vU = velCache[GetCacheIndex(uint3(pos + int3( 0,  1,  0)))];
float3 vB = velCache[GetCacheIndex(uint3(pos + int3( 0,  0, -1)))];
float3 vF = velCache[GetCacheIndex(uint3(pos + int3( 0,  0,  1)))];

float dx = BoundsSize.x / float(Resolution);
float div = ((vR.x - vL.x) + (vU.y - vD.y) + (vF.z - vB.z)) / (2.0 * dx);
```

隣のボクセルのデータを取得していますので、同じ情報を複数回取得することがかなりあります。
そのために私の実装だとキャッシュしています、最適化の所で説明します。

#### 圧力の計算

圧倒的に一番重い処理がこちらです。
ポアソン方程式を溶ける必要ですが、解析解が不可能です。
ですので、反復法でちょっとずつ近づく形にします。

基本的に以下の処理を~40回繰り返します。
これはヤコビ法っといいます

```hlsl
float pL = pressureCache[GetCacheIndex(uint3(pos + int3(-1,  0,  0)))];
float pR = pressureCache[GetCacheIndex(uint3(pos + int3( 1,  0,  0)))];
float pD = pressureCache[GetCacheIndex(uint3(pos + int3( 0, -1,  0)))];
float pU = pressureCache[GetCacheIndex(uint3(pos + int3( 0,  1,  0)))];
float pB = pressureCache[GetCacheIndex(uint3(pos + int3( 0,  0, -1)))];
float pF = pressureCache[GetCacheIndex(uint3(pos + int3( 0,  0,  1)))];

float dx = BoundsSize.x / float(Resolution);
float div = DivergenceRead[id];
PressureWrite[id] = (pL + pR + pD + pU + pB + pF - (dx * dx * div)) / 6.0;
```

もっと早いやり方がもちろん存在しいますが、これが一番実装しやすいやり方ですね。
今回は十分ですがマルチグリッド法などを実装するべきかと思います。

#### 圧力の力
外部の力と同じように速度に加算します。
ただ、発散が外部の力による影響がありまして、圧力が発散による影響があるため別のステップにする必要です。

```hlsl
float pL = pressureCache[GetCacheIndex(uint3(pos + int3(-1,  0,  0)))];
float pR = pressureCache[GetCacheIndex(uint3(pos + int3( 1,  0,  0)))];
float pD = pressureCache[GetCacheIndex(uint3(pos + int3( 0, -1,  0)))];
float pU = pressureCache[GetCacheIndex(uint3(pos + int3( 0,  1,  0)))];
float pB = pressureCache[GetCacheIndex(uint3(pos + int3( 0,  0, -1)))];
float pF = pressureCache[GetCacheIndex(uint3(pos + int3( 0,  0,  1)))];

float dx = BoundsSize.x / float(Resolution);
float3 vel = VelocityRead[id].xyz;
vel.x -= (pR - pL) / (2.0 * dx);
vel.y -= (pU - pD) / (2.0 * dx);
vel.z -= (pF - pB) / (2.0 * dx);
VelocityWrite[id] = float4(vel, 0.0);
```

#### 移流
理論上は移流が普通のゲームオブジェクトの動きと同じです、速度を位置に加算して終わりのはずです。
ですが、1つの大きい問題があります。

きれいに1つのボクセルが他のボクセルに移動するのわけがありません。
もっとあり得るのはボクセルの間まで移動します。

解決し方がどこまで行くではなく、どこから来たの計算を行います。
これもボクセルの間になりますが、ただのサンプリングで lerp した値を取得できます。

```hlsl
// +.5 to sample the center of the the voxel
float3 uvw = ((float3(id) + 0.5) / float(Resolution));
float3 uvwVel = VelocityRead[id].xyz / BoundsSize;
float3 prevUvw = uvw - (uvwVel * DeltaTime * SimScale);

float4 advectedSmokeProps = SmokePropRead.SampleLevel(sampler_LinearClamp, prevUvw, 0);
float4 advectedVel = VelocityRead.SampleLevel(sampler_LinearClamp, prevUvw, 0);
```

### エミッターについて
温度と煙のエミッターがとても分かりやすいですね。
ただそのボクセルのデータを上書きすれば大丈夫です。

```hlsl
if (insideEmitter(em, cellWorldPos))
{
    advectedSmokeProps.r = max(advectedSmokeProps.r, em.heat);
    advectedSmokeProps.g = max(advectedSmokeProps.g, em.density);
}
```

ですが、圧力のエミッターも実装してあります。
圧力が発散を抵抗するように実装してありますので、直接いじるのは難しいです。
でうすので、エミッターが発散を上書きすればいいです。

```hlsl

if (insideEmitter(em, worldPos))
{
    div -= em.expansion;
}
```

### 最適化
まだまだ可能ですが、とにかく影響大きいの1つの最適化を行いました。

#### 共有メモリのキャッシュ
ボクセルの隣のデータを取得するの時がかなりあります。
スレッドグループが `8 x 8 x 8` の場合は `8 x 8 x 8 x 6 = 3072` 回テクスチャをサンプルする。
ですがすべてのデータが `10 x 10 x 10 = 1000` ボクセルのキューブに入っています。
平均同じデータを3回読み込んでいます！

それを解決するためにまずその1000ボクセル分のデータを読み込んで、キャッシュする。
VRAMよりSRAMの読み込みが早いのでこれは全体的に早くなります。

### 見た目強化
これまでの炎がかなり不自然な物になりますね。問題が大体2つあります。

1. 自然の空気がちょっとでも、いつも動いています。これが炎に影響します。
2. ボクセルのシミュレーションするとボーテックスがどんどん弱まります。

1つずつ説明いたします。

#### 環境風
ノイズの関数が一番ですが、単純のサインウェーブの風でかなり見た目よくなります

```hlsl
float3 wind;
wind.x = sin(worldPos.y * AmbientWindScale + Time) * cos(worldPos.z * AmbientWindScale + Time);
wind.y = sin(worldPos.z * AmbientWindScale + Time) * cos(worldPos.x * AmbientWindScale + Time);
wind.z = sin(worldPos.x * AmbientWindScale + Time) * cos(worldPos.y * AmbientWindScale + Time);

vel += wind * AmbientWindSpeed * DeltaTime;
```

#### ボルティシティ・コンファイメント
空間と時間が区切るせいで、ボルテックスが弱まることが実はあります。
渦の感じがすごく流体っぽいので守りたい。そのために回転を計算して、ちょっと無理やり加算する

```hlsl
float wL = vorticityCache[GetCacheIndex(uint3(pos + int3(-1,  0,  0)))];
float wR = vorticityCache[GetCacheIndex(uint3(pos + int3( 1,  0,  0)))];
float wD = vorticityCache[GetCacheIndex(uint3(pos + int3( 0, -1,  0)))];
float wU = vorticityCache[GetCacheIndex(uint3(pos + int3( 0,  1,  0)))];
float wB = vorticityCache[GetCacheIndex(uint3(pos + int3( 0,  0, -1)))];
float wF = vorticityCache[GetCacheIndex(uint3(pos + int3( 0,  0,  1)))];

float dx = BoundsSize.x / float(Resolution);
float3 eta = float3(wR - wL, wU - wD, wF - wB) / (2.0 * dx);

if (length(eta) > 0.001)
{
    float3 N = normalize(eta);
    float3 curl = CurlRead[id].xyz;

    float3 vorticityForce = VorticityStrength * cross(N, curl) * dx;
    vel += vorticityForce * DeltaTime;
}
```

## 最低限の流体力学

### Navier-Stokes

流体力学とは、流れる物の動きの研究です。
みんなが学校で勉強した物体の動きのニュートン力学と違って、集団で動く物に関する領域です。

ニュートン力学の場合は有名な式で物の動きを計算できます。

$$
\bold{F} = m \bold{a}
$$

ですが、力学で一番ほしい情報は速度の変化です。微積分で書き直して、

$$
\frac{d \bold{v}}{d t} = \frac{1}{m} \bold{F}
$$

になります。

流体力学にも同じ役割の式がもちろんあります。

$$
\frac{D \bold{u}}{D t} = \nu \nabla^2 \bold{u} - \frac{1}{\rho}\nabla p + \frac{1}{\rho}\bold{f}
$$

ニュートン力学に比べたらかなり複雑ですが、形は似ています。
左側には速度の変化で、右側にはそれを影響することがあります。

項を1個ずつ説明します。

* $\frac{D \bold{u}}{D t}$: これは速度の導関数。流体の場合は $\bold{v}$ ではなく $\bold{u}$ を使います。あと導関数は $d$ ではなく $D$ で書きます。これについては後ほど説明します。
* $\nu \nabla^2 \bold{u}$: 粘性による力です。流体自体が動きに抵抗する力ですね。気体の場合は $\nu$ がほとんど $0$ なので、今回はこれを無視して大丈夫です。
* $\frac{1}{\rho}\nabla p$: 圧力による力です。流体の一部が周りの流体を押す力ですね。非圧縮性流体でもちゃんと存在します。
* $\frac{1}{\rho}\bold{f}$: 外部からの力です。流体の場合は質量の $m$ ではなく、質量密度の $\rho$ を使いますが、ニュートンの運動方程式と同じ役割です。

ニュートン力学に比べると、流体内部からの力が追加されていますが、まあ似たような式です。

実はこれは非圧縮性のケースです。圧力が変わっても体積（密度）が変わらないということです。
気体でも意外とこれで大丈夫です。密閉された箱の中やマッハに近い速度の場合はさすがにダメですが、一般的なケースでは気にしなくていいですよ。
爆発を表現したいならさすがにこれではアウトですが、完全に物理学に則ったシミュレーションは必要ありません。
まず非圧縮性で計算をしてから、物理学的に厳密でなくても爆発っぽい見た目を追加する、といった感じで問題ないです。


### 移流（Dの意志）

導関数では $d$ ではなく $D$ を使いましたが、これにはちゃんと意味があります。
これは物質微分（実質微分）というものです。

流体において速度を変化させるのは力だけでなく、流れていること自体によって粒子が動いていることも関係します。
欲しいのは特定の粒子の速度ではなく、「特定の場所」にある粒子の速度です。
粒子が速度（や温度などの他のパラメータ）を運んで別の場所に移動することを「移流」と言います。

物質微分は、ある粒子（流体要素）に沿った変化という意味です。
シミュレーションのためには、特定の場所での速度の変化が知りたいです。
それはこちらの式で計算できます。

$$
\frac{D \bold{u}}{D t} = \frac{\partial \bold{u}}{\partial t} + (\bold{u} \cdot \nabla)\bold{u}
$$

また、項ごとに説明します。

* $\frac{\partial \bold{u}}{\partial t}$: 特定の場所での速度の変化。計算したいのはこれです。
* $(\bold{u} \cdot \nabla)\bold{u}$: すごい書き方ですが、これは速度の移流です。流体の難しさは大体ここに詰まっています。

移流はまあ、計算というよりシミュレーションで処理するので深く気にしなくても大丈夫ですが、調べたい人はこの数文字の部分だけでも奥がかなり深いことがわかると思います。

### 発散と回転

ベクトル場の微分にはいくつか種類があります。
発散と回転の計算が必要になりますので、短めに説明します。

#### 発散

$$
\nabla \cdot \bold{F}
$$

ベクトルがある場所から湧き出していく（正の発散）か、集まっていく（負の発散）かを表します。
発散がちょうど0の場合は、単純にそのまま流れているイメージです。
特に今回は非圧縮性のシミュレーションなので、発散は0になるはずです。

#### 回転

$$
\nabla \times \bold{F}
$$

ベクトルがどれくらい渦を巻いているかの計算です。結果もベクトルになります。
物理学の授業で学んだ（はず）の右手の法則で、渦を巻く方向がわかります。
この渦の動きがすごく流体っぽいので、ちゃんと表現したいですね。

### 圧力の計算

非圧縮性の場合は、発散をゼロに保つ役割を果たすのが圧力です。
流れがある場所に集中しそうになったら、その場所の圧力が上がって、発散がゼロの状態を保つような感じです。
この条件を守るための圧力はポアソン方程式で計算します。

$$
\nabla^2 p = \nabla \cdot \bold{u}
$$

これは一発で計算できないやつなので、何回も反復して正しい圧力に近づけていくしかないです。
FPSが死ぬ原因は大体ここです。。。

### まとめ

流体シミュレーションのために空間をボクセルに分けて、各場所での $\frac{\partial \bold{u}}{\partial t}$ を計算します。
これを計算するには Navier-Stokes 方程式を以下のように利用します。

$$
\frac{\partial \bold{u}}{\partial t} = - (\bold{u} \cdot \nabla)\bold{u} - \frac{1}{\rho}\nabla p + \frac{1}{\rho}\bold{f}
$$

言葉にするとこれは、

```text
[速度の変化] = -[移流] - [圧力による力] + [外部からの力]
```

圧力のほうがポアソン方程式で計算します
$$
\nabla^2 p = \nabla \cdot \bold{u}
$$
