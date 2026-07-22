# NMBrowser（なみぶらうざ）

WebView2 を利用した Windows Forms 用のカスタムコントロールです。  
webview2を**UI 描画専用コンポーネント**として動作させます。

Windows Forms アプリケーションの画面を HTML で構築できるため、  
従来の WinForms では難しかった **リッチな UI を簡単に実装**できます。
NMBrowser（なみぶらうざ）は、UIにHTML/CSSのメリットを享受し、内部はC#のメリットを享受できるハイブリッドアプリケーションを実現します

本パッケージは **.NET 8 以降の Windows Forms アプリケーションで利用可能**です。
**.NET 7 / .NET 6 / .NET Framework には対応していません。**

## 概要

- WebView2 を UI レンダリング専用に利用  
- WinForms のフォーム上に配置して利用  
- ボタンやレイアウトなどを HTML/CSS で記述可能  
- 内部処理は、C#で記述可能


## インストール

Visual Studioの**NuGet パッケージ管理**からインストールできます。

1. プロジェクトを右クリック  
2. **NuGet パッケージの管理** を選択  
3. 「参照」タブでパッケージ名を検索  
4. **インストール** ボタンをクリック


### コマンドラインからの場合
.NET CLI:
```bash
dotnet add package NMBrowser
```

### パッケージ マネージャー コンソール:
```powershell
Install-Package NMBrowser
```


## 使い方（概要）

### 1. デザイナでカスタムコントロールを配置
フォームのデザイナ上に **NmBrowser** コントロールを配置します。  

### 2. フォームのコンストラクタで初期化
少なくとも 1 つ以上のページクラスを追加してから、`Initialize()` を呼び出します。

```csharp
public Form1()
{
    InitializeComponent();

    // ページを追加
    nmBrowser1.AddPage(new PageWelcome());

    // 初期化
    nmBrowser1.Initialize();
}

public class PageWelcome : NMWebPage
{
    public override void Draw()
    {
        Render(@"
            <h1>ようこそ</h1>
            <p>これが最初のページです</p>
        ");
    }
}
```

## 関連ドキュメント

[NuGetパッケージページ](https://www.nuget.org/packages/NMBrowser/)

[デモアプリケーション](https://github.com/omojikomoji/NMBrowserDemoApp)


## ライセンス

このパッケージは **Apache License 2.0** のもとで公開されています。
商業利用、改変、再配布は自由ですが、著作権表示の維持が必要です。

Copyright (c) 2026 omojikomoji  
Licensed under the Apache License, Version 2.0.

