# Elevated Explorer

SYSTEM / Administrator / TrustedInstaller トークンで動かすための、Explorerの
描画エンジン (IExplorerBrowser) を自前プロセスに埋め込んだミニExplorer。

## ビルド

```
dotnet build -c Release
```

`bin\Release\net48\SalonExplorer.exe` ができる。

## 使い方

AdvancedRunで `SalonExplorer.exe` を指定し、Run As を SYSTEM / Administrator /
TrustedInstaller などにして実行するだけ。実体は explorer.exe と無関係な自作プロセス
なので、リネームのような裏技は不要。

## 操作感

- 左ペイン: PC・クイックアクセスなどのツリー(通常Explorerと同じ)
- 右クリック: ネイティブのシェルコンテキストメニュー(プロパティ/セキュリティタブ含む)
- Backspace: 親フォルダへ
- Alt+←/→: 戻る/進む
- アドレスバー: パス直接入力+Enter/移動ボタン
- タイトルバー/タスクバーアイコン: explorer.exe から実行時に抽出(見た目がExplorerと揃う)

## カスタマイズ余地

- アイコンを完全に自前のものにしたい場合は `.ico` を用意して
  `SalonExplorer.csproj` に `<ApplicationIcon>app.ico</ApplicationIcon>` を追加
- アドレスバーの自動補完や「よく使う場所」バーなども ExplorerBrowser の
  NavigationOptions / PaneVisibility で追加調整可能
