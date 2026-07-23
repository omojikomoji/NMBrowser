using Microsoft.Web.WebView2.Core;
using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;

// Copyright (c) 2026 omojikomoji
// Licensed under the Apache License, Version 2.0.
// See LICENSE file in the project root for full license information.

namespace NMBrowser
{
    public sealed class NMBrowser : Microsoft.Web.WebView2.WinForms.WebView2
    {
        private const string _LOCAL_HOST = "nm.localhost";

        private Dictionary<string, NMWebPage> _pages { set; get; }
        private NMWebPage? _current_page { get; set; }
        private string _contents { get; set; }
        private Panel? _drop_panel;
        private string _hostname = string.Empty;
        private string _folderpath = string.Empty;
        private string _layout_base = string.Empty;

        private bool _isDebugMode = false;   // デバッグモードなら、ソースのDLと開発者ツールを認める

        public bool DebugMode
        {
            get { return _isDebugMode; } 
            set { _isDebugMode = value; } 
        }

        private Dictionary<string, string> _template_map { get; } = new Dictionary<string, string>();

        internal Dictionary<string, string> Template
        {
            get
            {
                return _template_map;
            }
        }

        public NMBrowser()
        {
            _pages = new Dictionary<string, NMWebPage>();

            _drop_panel = null;

            _contents = string.Empty;
        }

        public void Initialize()
        {
            InitializeAsync();

            // ここでbaseレイアウトをセットしておく
            SetLayoutBase(global::NMBrowser.Properties.Resources.nmcore);

            // イベントハンドラを設定
            this.WebMessageReceived += this.webView21_WebMessageReceived;
            this.CoreWebView2InitializationCompleted += this.webView21_CoreWebView2InitializationCompleted;
            this.NavigationStarting += this.webView21_NavigationStarting;
            this.NavigationCompleted += this.webView21_CoreWebView2NavigationCompleted;

            SetupDropPanel();
        }

        private async void InitializeAsync()
        {
            await EnsureCoreWebView2Async(null);

            // コンテキストメニュー禁止
            CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

            // ズームコントロールを無効にする
            CoreWebView2.Settings.IsZoomControlEnabled = false;

            // ステータスバーを非表示 (無効化) に設定します。
            CoreWebView2.Settings.IsStatusBarEnabled = false;

            // DevTools を無効化
            CoreWebView2.Settings.AreDevToolsEnabled = _isDebugMode;

            // ショートカットキーを無効にする
            CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = _isDebugMode;

            // ポップアップウィンドウは禁止する
            CoreWebView2.NewWindowRequested += (sender, e) =>
            {
                e.Handled = true;
            };


            if (string.IsNullOrWhiteSpace(_hostname) == false)
            {
                // ローカルディレクトリを仮想ホスト名にマッピング
                // ローカルディレクトリを仮想的なWebサーバーとしてWebView2に認識させ、
                // http://またはhttps://スキームでアクセスできるようにさせます

                // 仮想ホスト名のマッピング
                CoreWebView2.SetVirtualHostNameToFolderMapping(_hostname, _folderpath, CoreWebView2HostResourceAccessKind.Allow);
            }

        }

        public bool SetLocalFolderMapping(string folder)
        {
            return FolderMapping(_LOCAL_HOST, folder);
        }


        // ローカルファイルをWebView2に表示するための手段
        // <img src='C:\test\test.jpg'> これはセキュリティ機能でブロックされるので、マッピングする事で回避する。
        // 設定後は、下記URLでローカルファイルを参照できる。
        // <img src='http://{hostname}/test.jpg' />
        private bool FolderMapping(string hostname, string folder)
        {
            if (string.IsNullOrWhiteSpace(hostname)) return false;
            if (Directory.Exists(folder) == false) return false;

            _hostname = hostname;
            _folderpath = folder;

            return true;
        }


        // コントロールの初期化後に、一回のみ呼び出される
        private void webView21_CoreWebView2InitializationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2InitializationCompletedEventArgs e)
        {
            // 初期ページの表示
            if (_current_page != null)
            {
                _current_page.Draw();
            }
        }

        private void webView21_NavigationStarting(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
        {
            // e.Uriには、ドメイン指定が必要。ドメインがない場合は、{about:blank#blocked}でくる
            // <base href='http://nmlocal/'>
            // ↑HTMLにこれを記述しておくと、ドメインが自動で補完される。つまり相対パスで指定しても、正しくURLが渡ってくる
            var url = e.Uri.ToString();

            // http://nmlocal/で始まるURLなら、メソッドとして実行
            if (url.StartsWith($"http://{_LOCAL_HOST}/"))
            {
                ExecuteMethod(url);

                // ページ遷移はキャンセル
                e.Cancel = true;
            }
            else if (url.StartsWith("data") == false)
            {
                // 外部サイトへのアクセスは禁止
                // ブラウザのデフォルト404を防ぐために、画像以外のNavigateイベントは全てキャンセルする

                // dataで始まらないURLはすべてキャンセル
                e.Cancel = true;
            }
        }

        private void webView21_CoreWebView2NavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.HttpStatusCode == 404)
            {
                CoreWebView2.NavigateToString(
                    "<h1>Custom 404</h1><p>ページが見つかりません。</p>");
            }
        }

        private void webView21_WebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            string json = "";

            try
            {
                json = e.WebMessageAsJson;
                json = json.Trim('"');

                // ドロップパネルの処理はページではなく、ここで処理する
                if (json.Contains("NMBodyDragover"))
                {
                    ShowDropPanel(true);
                    return;
                }

                if (json.Contains("NMBodyDragleave"))
                {
                    ShowDropPanel(false);
                    return;
                }

                // URIクラスには、ドメインが必要なので、http://nmlocal/を補完する。
                ExecuteMethod(string.Format("http://{0}/{1}", _LOCAL_HOST, json));
            }
            catch (Exception ex)
            {
                var contents = string.Format("<html><body><h1>Exception!!!</h1>{0}</body></html>", ex.ToString());
                CoreWebView2.NavigateToString(contents);
            }
        }

        private void SetupDropPanel()
        {
            // カスタムパネル（＝透明パネル）を使わないのであれば、ここを通常のパネルクラスにすることで
            // 動作させることができます。
            // その場合、ドロップを有効にすると、グレーのパネルが表示されます。
            //_drop_panel = new Panel();

            // 透明パネル
            _drop_panel = new TransparentPanel();

            _drop_panel.SuspendLayout();

            _drop_panel.AllowDrop = true;
            _drop_panel.BackColor = SystemColors.Control;
            _drop_panel.BorderStyle = BorderStyle.FixedSingle;
            _drop_panel.Location = new Point(0, 0);
            _drop_panel.Name = "drop_panel";
            _drop_panel.TabIndex = 1;
            _drop_panel.Visible = false;
            _drop_panel.DragDrop += DropPanelDragDrop;
            _drop_panel.DragEnter += DropPanelDragEnter;
            _drop_panel.Size = new Size(517, 311);

            this.Controls.Add(_drop_panel);
        }

        internal void SetLayoutBase(string layoutbase)
        {
            _layout_base = layoutbase;
        }

        // テンプレートを設定する
        public void SetTemplate(string templateName, string template)
        {
            if (_template_map.ContainsKey(templateName)) return;
            _template_map[templateName] = template;
        }

        internal Form? GetParent()
        {
            if (this.Parent == null) return null;
            return (Form)this.Parent;
        }

        internal void SetCurrentPage(NMWebPage page)
        {
            _current_page = page;
        }

        public string GetSource()
        {
            // NMCoreソースを削除する
            // （ローカル保存ではエラーが出るため）
            var source = Regex.Replace(
                    _contents,
                    @"<!-- BEGIN_OF_NM_CORE -->.*?<!-- END_OF_NM_CORE -->",
                    "class NMCore{static init(a){}static call(a,b){}}",
                    RegexOptions.Singleline);

            return source;
        }

        public void AddPage(NMWebPage page)
        {
            if (page == null) return;

            if (_pages == null)
            {
                _pages = new Dictionary<string, NMWebPage>();
            }

            Type type = page.GetType();
            _pages.Add(type.Name, page);

            page.SetBrowser(this);

            // 最初に追加したページをスタートページとして扱う
            if (_current_page == null)
            {
                _current_page = page;
            }
        }

        public NMWebPage? GetPage(string page_name)
        {
            NMWebPage? page = null;

            if (string.IsNullOrWhiteSpace(page_name)) return page;

            if (_pages.ContainsKey(page_name))
            {
                page = _pages[page_name];
            }

            return page;
        }

        internal void PostMessage(string message)
        {
            // 文字列で送るなら
            //this.CoreWebView2.PostWebMessageAsString(message);

            // JSONで送るなら
            this.CoreWebView2.PostWebMessageAsJson(message);
        }

        private void ExecuteMethod(string url)
        {
            Uri uri = new Uri(url);

            // url = uri.AbsolutePath;
            string class_name = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
            string method_name = Path.GetExtension(uri.AbsolutePath).Substring(1);

            if ((string.IsNullOrWhiteSpace(class_name)) || (string.IsNullOrWhiteSpace(method_name))) return;

            NMWebPage? page = GetPage(class_name);
            if (page == null) return;

            // 自力でクエリを取得する
            int index = uri.ToString().IndexOf("?");
            if (index > 0)
            {
                string query = uri.ToString().Substring(index);
                page.SetQuery(query);
            }

            // 呼び出したいクラスの型を取得
            Type type = page.GetType();

            // メソッド情報を取得
            // 　クラス名は、大文字小文字を区別します。
            //   public メソッドを対象に検索
            //   publicな同名関数が、複数定義されていれば、実行時に例外が発生します。
            //
            MethodInfo? methodInfo = type.GetMethod(method_name);
            if (methodInfo != null)
            {
                methodInfo.Invoke(page, new object[] { });
            }
        }

        internal void ShowError(string msg)
        {
            CoreWebView2.NavigateToString(msg);
        }

        internal string DrawDocument(string contents)
        {
            try
            {
                // コアレイアウトの適用
                if (string.IsNullOrWhiteSpace(_layout_base) == false)
                {
                    contents = _layout_base.Replace("<%contents%>", contents);
                }

                // ページクラス名を設定する。
                if (_current_page != null)
                {
                    contents = contents.Replace("<%current_page_name%>", _current_page.PageName);
                }

                // ホスト名を埋め込む
                contents = contents.Replace("<%base_hostname%>", string.Format("<base href='http://{0}/'>", _LOCAL_HOST));

                // デバッグモードの設定
                contents = contents.Replace("<%debug_mode%>", (_isDebugMode) ? "true" : "false");

                // 置換できなかったプレースホルダーを削除
                contents = Regex.Replace(contents, @"<%.*?%>", "");

                // テンプレートタグを除去
                contents = Regex.Replace(contents,@"<nm_template>.*?</nm_template>","",RegexOptions.Singleline);

                // WebView2コントロールにHTML文字列を読み込む
                CoreWebView2.NavigateToString(contents);
            }
            catch (Exception ex)
            {
                ShowError(ex.ToString());
            }

            _contents = contents;

            return contents;
        }

        // -----------------------------------------------------
        //
        // Javascriptの実行関連　ここから
        //
        // -----------------------------------------------------

        // javaScriptを、そのまま実行する
        internal async Task<T> Javascript<T>(string script)
        {
            if (string.IsNullOrWhiteSpace(script))
            {
                return default!;
            }

            var result = await CoreWebView2.ExecuteScriptAsync(script);

            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<T>(result)!;
            }
            catch
            {
                // デシリアライズに失敗した場合
                return (T)Convert.ChangeType(result.Trim('"'), typeof(T));
            }
        }


        internal async Task<T> Javascript<T>(string func, params object[] args)
        {
            if (string.IsNullOrWhiteSpace(func)) return default!;

            var param = new List<string>();
            foreach (var item in args)
            {
                if (item is null)
                {
                    param.Add("null");
                    continue;
                }

                try
                {
                    param.Add(System.Text.Json.JsonSerializer.Serialize(item));
                }
                catch
                {
                    try
                    {
                        // シリアライズに失敗したら、安全に文字列化して「JSの文字列」として渡す
                        // この時、念のためダブルクォーテーションをエスケープして囲む
                        var safeString = item.ToString()?.Replace("\"", "\\\"") ?? "";
                        param.Add($"\"{safeString}\"");
                    }
                    catch (Exception ex)
                    {
                        param.Add($"\"[Serialization & ToString Failed: {ex.GetType().Name}]\"");
                    }
                }
            }

            // 引数をカンマ区切りで結合
            var script = $"{func}({string.Join(", ", param)})";

            // スクリプト実行
            var result = await CoreWebView2.ExecuteScriptAsync(script);

            // WebView2の返却値は、JSONとして返ってくる
            if (string.IsNullOrEmpty(result) || result == "null")
            {
                return default!;
            }

            try
            {
                // 戻り値のデシリアライズ
                return System.Text.Json.JsonSerializer.Deserialize<T>(result)!;
            }
            catch
            {
                // デシリアライズに失敗した場合
                return (T)Convert.ChangeType(result.Trim('"'), typeof(T));
            }
        }

        // -----------------------------------------------------
        //
        // Javascriptの実行関連　ここまで
        //
        // -----------------------------------------------------

        private void ShowDropPanel(bool is_visible)
        {
            if (_drop_panel == null) return;

            //_drop_panel.Size = new Size((int)(this.Width * 0.8), (int)(this.Height * 0.8));
            _drop_panel.Size = new Size((int)(this.Width), (int)(this.Height)); // 完全に覆う

            // 中心に表示する
            _drop_panel.Left = (int)((this.Width - _drop_panel.Width) * 0.5);
            _drop_panel.Top = (int)((this.Height - _drop_panel.Height) * 0.5);

            _drop_panel.Visible = is_visible;
        }

        private void DropPanelDragDrop(object? sender, DragEventArgs e)
        {
            if (_current_page == null) return;
            if (_drop_panel == null) return;
            if (sender == null) return;

            _current_page.OnDragDrop(sender, e);

            _drop_panel.Visible = false;
        }

        private void DropPanelDragEnter(object? sender, DragEventArgs e)
        {
            if (e.Data == null) return;

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Move;
            }
            else
            {
                // ファイル以外のドロップ
                //_drop_panel.Visible = false;
                e.Effect = DragDropEffects.None;
            }
        }

    }       // end of NMBrowser


    public abstract class NMWebPage
    {
        private NMBrowser? _browser;

        private bool _allow_drop_file = false;
        public bool AllowDropFile
        {
            get { return _allow_drop_file; }
            set { _allow_drop_file = value; }
        }

        private NMParam _pa { get; set; }

        private NMScreen _sc { get; set; }

        public string PageName
        {
            get
            {
                return this.GetType().Name;
            }
        }

        public NMWebPage()
        {
            _browser = null;
            _allow_drop_file = false;

            _pa = new NMParam();
            _sc = new NMScreen();
        }

        virtual public void OnLoad()
        {
        }

        virtual public void OnBeforeUnload()
        {
        }

        public abstract void Draw();

        virtual public void OnDragDrop(object sender, DragEventArgs e)
        {
        }

        internal void SetBrowser(NMBrowser browser)
        {
            _browser = browser;
        }

        protected void PostMessage(string eventName, params object[] args)
        {
            string json = "";

            if (args.Length == 0)
            {
                // 引数なし
                json = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>()
                                                                    {
                                                                        { "event", eventName },
                                                                        { "args", "" }
                                                                    });
            }
            else if (args.Length == 1)
            {
                // 可変引数のサイズが１なら、配列ではなくそのものを送る
                json = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>()
                                                                    {
                                                                        { "event", eventName },
                                                                        { "args", args[0] }
                                                                    });
            }
            else
            {
                // 配列ならそのまま配列で送る
                json = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>()
                                                                    {
                                                                        { "event", eventName },
                                                                        { "args", args }
                                                                    });
            }


            if (_browser != null) _browser.PostMessage(json);
        }

        protected Rectangle GetWindowSize()
        {
            if (_browser == null) return new Rectangle(0, 0, 0, 0);
            return new Rectangle(0, 0, _browser.Width, _browser.Height);
        }

        protected void ShowError(string msg)
        {
            if (_browser != null) _browser.ShowError(msg);
        }


        protected string Render(string contents)
        {
            if (_browser == null) return string.Empty;

            _browser.SetCurrentPage(this);

            // ファイルのドロップ許可はページ単位で設定する
            _browser.AllowExternalDrop = _allow_drop_file;

            // 共通テンプレートを埋め込む
            contents = NMLayout.ApplyTemplate(contents, _browser.Template);

            // 値を埋め込む
            contents = NMLayout.GetContents(contents, _sc.Data);
            _sc.Clear();

            return _browser.DrawDocument(contents);
        }

        protected string Render(string contents, Dictionary<string, string> map)
        {
            if (_browser == null) return string.Empty;

            _browser.SetCurrentPage(this);

            // ファイルのドロップ許可はページ単位で設定する
            _browser.AllowExternalDrop = _allow_drop_file;

            // 共通テンプレートを埋め込む
            contents = NMLayout.ApplyTemplate(contents, _browser.Template);

            // 値を埋め込む
            contents = NMLayout.GetContents(contents, map);

            return _browser.DrawDocument(contents);
        }

        protected Form? GetParent()
        {
            if (_browser == null) return null;

            return _browser.GetParent();
        }

        // -----------------------------------------------------
        //
        // Javascriptの実行関連　ここから
        //
        // -----------------------------------------------------
        
        // 関数定義名と引数を渡し、JS関数を実行します
        protected async Task<string> Callback(string name, params object[] args)
        {
            return await Callback<string>(name, args);
        }

        // 関数定義名と引数を渡し、JS関数を実行します
        protected async Task<T> Callback<T>(string name, params object[] args)
        {
            // 定義名から関数を取得する
            var func = Get(name);

            // JS関数として、実行する
            return await Javascript<T>(func, args);
        }

        
        // 引数のスクリプトをそのまま実行します
        // string型固定
        protected async Task<string> Javascript(string script)
        {
            return await Javascript<string>(script);
        }
        

        // 引数のスクリプトをそのまま実行します
        // ジェネリック型
        protected async Task<T> Javascript<T>(string script)
        {
            if (_browser == null) return default!;

            // functionから始まるのは、無名関数である
            //
            // 無名関数　：　function(arg){ alert(arg) }　を実行するには、
            // (無名関数)(引数)　の形で実行する必要がある。
            //
            // (function(arg){ alert(arg) })('hoge')
            //
            if ((script.Trim().StartsWith("function")) || 
                (script.Trim().StartsWith("async function")))
            {
                // 無名関数の場合、()で括らないと、引数渡しの実行ができない
                script = string.Format("({0})", script);
            }

            return await _browser.Javascript<T>(script);
        }

       
        // --------------------------
        protected async Task<string> Javascript(string func, params object[] args)
        {
            return await Javascript<string>(func, args);
        }
        
        // --------------------------
        protected async Task<T> Javascript<T>(string script, params object[] args)
        { 
            if (_browser == null) return default!;
            if (string.IsNullOrWhiteSpace(script)) return default!;


            // functionから始まるのは、無名関数である
            //
            // 無名関数　：　function(arg){ alert(arg) }　を実行するには、
            // (無名関数)(引数)　の形で実行する必要がある。
            //
            // (function(arg){ alert(arg) })('hoge')
            //
            if ((script.Trim().StartsWith("function")) ||
                (script.Trim().StartsWith("async function")))
            {
                // 無名関数の場合、()で括らないと、引数渡しの実行ができない
                script = string.Format("({0})", script);
            }

            return await _browser.Javascript<T>(script, args);
        }

        // -----------------------------------------------------
        //
        // Javascriptの実行関連　ここまで
        //
        // -----------------------------------------------------

        protected string GetSource()
        {
            if (_browser == null) return string.Empty;

            var source = _browser.GetSource();

            return source;
        }

        // デバッグ用にソースをDLする
        public async void DownloadSource()
        {
            var source = GetSource();

            string json = System.Text.Json.JsonSerializer.Serialize(source);

            await Callback("done", $"{this.PageName}_SRC_.html", json);
        }


        protected Point PointToClient(int px, int py)
        {
            Point cp = new Point(px, py);

            // クライアント領域の絶対位置(スクロール量は加味しない)
            return PointToClient(cp);
        }

        protected Point PointToClient(Point p)
        {
            if (_browser == null) return new Point(0, 0);
            return _browser.PointToClient(p);
        }

        protected Point PointToScreen(int px, int py)
        {
            Point cp = new Point(px, py);
            return PointToScreen(cp);
        }

        protected Point PointToScreen(Point p)
        {
            if (_browser == null) return new Point(0, 0);
            return _browser.PointToScreen(p);
        }

        //
        // 送信されたFormデータを処理する
        //
        internal void SetQuery(string query)
        {
            _pa.SetQuery(query);
        }

        protected Dictionary<string, string[]> GetQuery(string tag = "", bool remove_prefix = false)
        {
            return _pa.GetQuery(tag, remove_prefix);
        }

        // ------------------------------------------------------------------------------------------------------------- //
        // paramクラスにあればそれを返し、なければJSで取得を試みる

        protected async ValueTask<string> GetAsync(string key, string def_val = "")
        {
            return await GetAsync<string>(key, def_val);
        }

        protected async ValueTask<T> GetAsync<T>(string key, T def_val = default!)
        {
            if (_pa.IsExist(key))
            {
                return _pa.Get<T>(key);
            }

            // JSから取得する（この時だけ非同期）
            return await GetValueAsync<T>(key, def_val);
        }


        protected string Get(string key, string def_val = "")
        {
            return _pa.Get(key, def_val);
        }

        protected T Get<T>(string key, T def_val = default!)
        {
            return _pa.Get<T>(key, def_val);
        }


        // 直接要素から、値を取得する
        private async Task<T> GetValueAsync<T>(string elementName, T def_val = default!)
        {
            try
            {
                var json = await Javascript<string>($"NMCore.getFormValues('{elementName}')");

                var values = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
                if ((values == null) || (values.Count == 0)) return def_val;

                Type type = typeof(T);
                if (type.IsArray)
                {
                    // 配列で返す
                    Type elemType = type.GetElementType()!;
                    Array arr = Array.CreateInstance(elemType, values.Count);

                    for (int i = 0; i < values.Count; i++)
                    {
                        object val = Convert.ChangeType(values[i], elemType);
                        arr.SetValue(val, i);
                    }

                    return (T)(object)arr;
                }
                else
                {
                    // 先頭の値を型に変換して返す
                    return (T)Convert.ChangeType(values[0].Trim(), typeof(T));
                }
            }
            catch
            {
                throw new InvalidCastException();
            }
        }
        // ------------------------------------------------------------------------------------------------------------- //


        protected void ClearCache()
        {
            _sc.Clear();
        }

        // 画面に値を設定する
        protected void Attach<T>(string key, T value)
        {
            _sc.Set<T>(key, value);
        }

        protected void Attach(Dictionary<string, string> add)
        {
            _sc.Set(add);
        }

        // NMPageクラスの定義はここまで

        // ここからは、内部クラスの定義

        private class NMScreen
        {
            private Dictionary<string, string> _data_map = new Dictionary<string, string>();

            public Dictionary<string, string> Data
            {
                get
                {
                    return _data_map;
                }
            }

            public void Clear()
            {
                _data_map.Clear();
            }

            public void Set<T>(string key, T value)
            {
                try
                {
                    _data_map[key] = (value == null) ? "" : value.ToString();
                }
                catch
                {
                    _data_map[key] = "";
                }
            }

            public void Set(Dictionary<string, string> add)
            {
                if (add == null)
                {
                    return;
                }

                foreach (var kv in add)
                {
                    _data_map[kv.Key] = (kv.Value == null) ? "" : kv.Value;
                }
            }
        } // end of NMScreen

        private static class NMLayout
        {
            public static string GetContents(string contents, Dictionary<string, string> map)
            {
                contents = FloodValue(contents, map);

                return contents;
            }

            public static string ApplyTemplate(string contents, Dictionary<string, string> template_map)
            {
                if (template_map.Count > 0)
                {
                    // テンプレートを埋め込む
                    Regex rgx = new Regex("<nm_template>.*?</nm_template>", RegexOptions.IgnoreCase);
                    MatchCollection matches = rgx.Matches(contents);
                    if (matches.Count > 0)
                    {
                        foreach (Match match in matches)
                        {
                            string template_name = match.Value.Replace("<nm_template>", "").Replace("</nm_template>", "");
                            if (string.IsNullOrWhiteSpace(template_name)) continue;

                            if (template_map.ContainsKey(template_name))
                            {
                                contents = contents.Replace(match.Value, template_map[template_name]);
                            }
                        }
                    }
                }

                return contents;
            }

            // HTML特殊文字をエスケープして返す
            public static string esc(string value)
            {
                string[] html_special_chars = { "&", "&amp;",
                                                "<", "&lt;",
                                                ">", "&gt;",
                                                "\"", "&quot;",
                                                "'", "&#39;"};

                for (int i = 0; i < html_special_chars.Length; i += 2)
                {
                    value = value.Replace(html_special_chars[i], html_special_chars[i + 1]);
                }

                return value.Replace("\n", "<br />");
            }

            private static string FloodValue(string contents, string placeholder, string value)
            {
                // プレースホルダーに . や + が入っても安全に処理できるようにエスケープする
                string ph = Regex.Escape(placeholder);

                // <% placeholder %> → エスケープ値
                contents = Regex.Replace(
                    contents,
                    $@"<%\s*{ph}\s*%>",     // <% name %> など半角SPは許容する
                    esc(value)
                //,RegexOptions.IgnoreCase     // 大文字小文字を無視する
                );

                // <%{ placeholder }%> → 生値
                contents = Regex.Replace(
                    contents,
                    $@"<%\{{\s*{ph}\s*\}}%>",       // <% name %> など半角SPは許容する
                    value
                //,RegexOptions.IgnoreCase     // 大文字小文字を無視する
                );

                return contents;
            }
            private static string FloodValue(string contents, Dictionary<string, string> dict)
            {
                foreach (var kv in dict)
                {
                    string placeholder = kv.Key;
                    string value = kv.Value;

                    // プレースホルダーに . や + が入っても安全に処理できるようにエスケープする
                    string ph = Regex.Escape(placeholder);

                    // <% placeholder %> → エスケープ値
                    contents = Regex.Replace(
                        contents,
                        $@"<%\s*{ph}\s*%>",      // <% name %> など半角SPは許容する
                        esc(value)
                    );

                    // <%{ placeholder }%> → 生値
                    contents = Regex.Replace(
                        contents,
                        $@"<%\{{\s*{ph}\s*\}}%>",        // <% name %> など半角SPは許容する
                        value
                    );
                }

                return contents;
            }

        }   // end of NMLayout


        private class NMParam
        {
            private Dictionary<string, List<string>> _query_map;

            // コンストラクタ
            public NMParam()
            {
                _query_map = new Dictionary<string, List<string>>();
                _query_map.Clear();
            }

            internal void SetQuery(string query)
            {
                _query_map.Clear();

                query = query.TrimStart('?');

                if (query != string.Empty)
                {
                    // エスケープされた文字列を元の文字列に変換します。
                    // エスケープシーケンス（\t、\\ など）も適切に処理する。
                    //query = Regex.Unescape(query);

                    string[] param_data = query.Split('&');
                    foreach (string param in param_data)
                    {
                        if (string.IsNullOrWhiteSpace(param)) continue;

                        // ここでやっとデコードできる
                        var value = System.Web.HttpUtility.UrlDecode(param);

                        int index = value.IndexOf('=');
                        if (index == -1) continue;

                        string key = value.Substring(0, index);
                        string val = value.Substring(index + 1);

                        if (string.IsNullOrWhiteSpace(key)) continue;

                        if (_query_map.ContainsKey(key) == false)
                        {
                            _query_map.Add(key, new List<string> { val });
                        }
                        else
                        {
                            // 同じキーが複数送信された場合、配列として管理する
                            _query_map[key].Add(val);
                        }
                    }
                }
            }

            public Dictionary<string, string[]> GetQuery(string tag = "", bool remove_prefix = false)
            {
                var map = new Dictionary<string, string[]>();

                foreach (KeyValuePair<string, List<string>> kvp in _query_map)
                {
                    var name = kvp.Key;

                    if (string.IsNullOrWhiteSpace(tag) == false)
                    {
                        if (name.StartsWith(tag) == false) continue;
                    }

                    if (remove_prefix)
                    {
                        name = name.Replace(tag, "");
                    }

                    map[name] = kvp.Value.ToArray();
                }

                return map;
            }

            public bool IsExist(string key)
            {
                if( _query_map.ContainsKey(key))
                {
                    return true;
                }
                return false;
            }

            public string Get(string key, string def_val = "")
            {
                if (_query_map.ContainsKey(key))
                {
                    return _query_map[key][0].Trim();
                }

                return def_val;
            }

            //
            // defaultは、その型のデフォルト値
            // 参照型なら null。値型なら その型のゼロ値
            public T Get<T>(string key, T def_val = default(T)!)
            {
                if (_query_map.ContainsKey(key) == false) return def_val;

                try
                {
                    Type type = typeof(T);

                    if (type.IsArray)
                    {
                        // 配列で返す
                        var vals = _query_map[key];

                        Type elemType = type.GetElementType()!;
                        Array arr = Array.CreateInstance(elemType, vals.Count);

                        for (int i = 0; i < vals.Count; i++)
                        {
                            // Tの中身の型に変換して、Tに突っ込む
                            object val = Convert.ChangeType(vals[i], elemType);
                            arr.SetValue(val, i);
                        }

                        return (T)(object)arr;
                    }
                    else
                    {
                        // 先頭の値を型に変換して返す
                        return (T)Convert.ChangeType(_query_map[key][0].Trim(), typeof(T));
                    }

                }
                catch
                {
                    throw new InvalidCastException();
                }
            }

        } // end of NMParam


    } // end of NMWebPage

    [DesignTimeVisible(false)]
    public class TransparentPanel : Panel
    {
        public TransparentPanel()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);

            this.BackColor = Color.Transparent;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000020;
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
        }

        protected override void OnMove(EventArgs e)
        {
            InvalidateParent();
            base.OnMove(e);
        }

        protected override void OnResize(EventArgs e)
        {
            InvalidateParent();
            base.OnResize(e);
        }

        private void InvalidateParent()
        {
            if (Parent == null) return;

            Rectangle rc = new Rectangle(this.Location, this.Size);
            Parent.Invalidate(rc, true);
        }

    }

}
