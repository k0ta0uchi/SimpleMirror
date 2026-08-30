# SimpleMirror 🪞📱

[English](#english) | [日本語](#日本語)

---

<a name="english"></a>
## English

**SimpleMirror** is a high-performance, ultra-low latency AirPlay receiver and iOS screen mirroring desktop application crafted with a modern, fluid glass aesthetic for Windows (.NET 9 / WPF / Direct3D).

### ✨ Key Features

- ✨ **Fluid Modern UI & Tactile Physics**:
  - Tactile micro-scale button animations (`scale(0.95)`) for instant pointer-down feedback.
  - Liquid Glass translucent materials with light-catching top specular highlights.
  - Continuous squircle curves (`CornerRadius="16"`) and floating pill status indicators.
  - Smooth animated toggle switches in settings.
- ⚡ **3-Tier Performance Profiles**:
  - **⚡ Ultra Low Latency (Performance)**: 60fps with presentation buffer bypass (`-vsync no`). Eliminates iPhone downscaling jitter, perfect for rhythm & action games.
  - **⚖️ Balanced (Standard)**: Crisp 1080p @ 60fps, optimal for browsing, presentations, and streaming.
  - **💎 High Quality (Quality)**: 1440p (2K) @ 60fps with high-precision color interpolation for photos and videos.
- 🎬 **OBS Studio Clean View Mode (`F10` / `Ctrl+H`)**:
  - Strips all window chrome (title bar, toolbar, status bar, and borders) for a 100% pure video canvas.
  - Seamlessly captured via OBS Studio Window Capture without manual crop or padding adjustments.
- 🔄 **Smart Auto & Manual Rotation**:
  - In-place scaling without TCP reconnects or connection drops.
  - Multi-monitor aware (`MonitorFromWindow`): never jumps from secondary to primary displays upon rotation.
  - Center-anchor window resizing preserving visual alignment.
  - One-click `[ ✓ Auto ]` toggle on the toolbar to prevent unexpected orientation switches.
- 📸 **Direct Snapshot (`Ctrl+S`)**:
  - One-click GPU-rendered frame capture saved to disk and automatically copied to your clipboard.
- 🛡️ **Zero-Config Bonjour & Firewall Integration**:
  - Built-in UDP multicast DNS (mDNS) announcer + standard dnssd integration.
  - Automatic UAC firewall configuration helper.

### ⌨️ Keyboard Shortcuts

| Shortcut | Action |
| :--- | :--- |
| **`F11`** | Toggle Fullscreen Mode |
| **`F10`** or **`Ctrl + H`** | Toggle OBS Clean View Mode |
| **`Escape`** | Exit Fullscreen / Clean View directly |
| **`Ctrl + R`** | Rotate screen (Portrait ⇄ Landscape) |
| **`Ctrl + S`** | Take Screenshot & Copy to Clipboard |
| **`Ctrl + T`** | Toggle Always on Top |

### 🚀 Getting Started

#### Requirements
- Windows 10 (1809+) or Windows 11 (x64)
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- iPhone / iPad connected to the same local Wi-Fi network

#### Build & Run
```powershell
# Clone or navigate to the repository
cd C:\Workspace\SimpleMirror

# Build and launch
dotnet run
```

---

<a name="日本語"></a>
## 日本語

**SimpleMirror** は、洗練されたモダンUI（触覚フィードバック、Liquid Glass、連続曲線スクワークル）を取り入れた、Windows (.NET 9 / WPF / Direct3D) 向けの超低遅延・高画質 AirPlay レシーバー & iOS 画面ミラーリングアプリです。

### ✨ 主な機能

- ✨ **洗練されたモダンUI & 触覚フィードバック**:
  - ボタン押下時のマイクロスケール（`scale(0.95)`）スプリングアニメーションによる吸い付くような操作感。
  - 上端に光が当たる極薄のスペキュラーハイライト境界線と Liquid Glass マテリアル。
  - 連続曲線（`CornerRadius="16"`）と呼吸するように発光するフローティングステータスカプセル。
  - スムーズなスライディングトグルスイッチ（環境設定）。
- ⚡ **3段階の動作プロファイル**:
  - **⚡ 超低遅延 / パフォーマンス**: 60fps / 垂直同期待機スキップ（`-vsync no`）。iPhone のダウンスケール負荷を排除し、音ゲーやアクションゲームでも滑らかに追従。
  - **⚖️ 標準 / バランス**: 1080p @ 60fps。文字の視認性と滑らかさ、低遅延のベストバランス。
  - **💎 高画質 / クオリティ**: 1440p (2K) @ 60fps。最高精細のカラー補間による写真・動画鑑賞向け。
- 🎬 **OBS Studio 配信用クリーン表示モード (`F10` / `Ctrl+H`)**:
  - タイトルバー・操作バー・ステータスバー・枠線を全消去し、純粋な iPhone 映像のみを表示。
  - OBS Studio の「ウィンドウキャプチャ」でクロップ（切り抜き）調整なしに直接クリーンキャプチャ可能。
- 🔄 **マルチモニター対応 & 自動/手動回転**:
  - TCP 再接続なしのインプレース拡大縮小（切断ゼロ）。
  - サブモニター配置時に回転してもプライマリモニターへ勝手に移動しないマルチモニター境界判定。
  - ウィンドウの中心位置を維持して拡大するセンターアンカーリサイズ。
  - ツールバーの **`[ ✓ 自動 ]`** チェックで勝手な回転をいつでもワンクリックでロック可能。
- 📸 **ワンクリック・スクリーンショット (`Ctrl+S`)**:
  - ダイレクト GPU キャプチャ画像をピクチャフォルダに自動保存し、同時にクリップボードへコピー。
- 🛡️ **ゼロコンフィグ Bonjour & ファイアウォール自動構成**:
  - 標準 dnssd + 独自 UDP mDNS マルチキャストによる即時検出。
  - ワンクリック UAC 昇格によるファイアウォール自動許可。

### ⌨️ ショートカットキー一覧

| ショートカット | 機能 |
| :--- | :--- |
| **`F11`** | 全画面表示の切り替え |
| **`F10`** / **`Ctrl + H`** | OBS 配信用クリーンモードの切り替え |
| **`Escape`** | 全画面 / クリーンモードの即時解除 |
| **`Ctrl + R`** | 画面の向きを手動回転 (縦 ⇄ 横) |
| **`Ctrl + S`** | スクリーンショット撮影（クリップボード自動コピー） |
| **`Ctrl + T`** | 常に最前面表示 (Always on Top) の切り替え |

### 🚀 起動方法

#### 必要環境
- Windows 10 (1809以降) / Windows 11 (x64)
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- 同一 Wi-Fi に接続された iPhone / iPad

#### ビルドと実行
```powershell
cd C:\Workspace\SimpleMirror
dotnet run
```

### 📱 使い方
1. SimpleMirror を起動します。
2. iPhone のコントロールセンターを開き、**「画面ミラーリング (2つの四角形)」** をタップします。
3. 一覧から **「SimpleMirror」** を選択すると、即座にミラーリングが開始されます。

---

## 📄 License
MIT License
