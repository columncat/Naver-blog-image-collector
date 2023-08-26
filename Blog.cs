using Newtonsoft.Json;
using System.Data.SQLite;
using HtmlAgilityPack;
using System;
using System.Net;
using System.Web;
using System.IO;

public class Blog
{
    private string blogId;
    private int totalPosts;
    private int totalImages;
    private Dictionary<int, string> categories;
    private List<Post> posts;
    private bool isBlogInfoGathered;
    private bool isPostsGathered;
    private bool isImagesGathered;
    private List<string[]> images;


    private Blog(string blogId)
    {
        this.blogId = blogId;
        this.totalPosts = 0;
        this.totalImages = 0;
        this.posts = new List<Post>();
        this.categories = new Dictionary<int, string>();
        this.isBlogInfoGathered = false;
        this.isPostsGathered = false;
        this.isImagesGathered = false;
        this.images = new List<string[]>();
    }

    private async Task GetBlogInfoAsync()
    {
        Console.Write("1. Gathering blog information\n");

        HttpClient httpClient = new HttpClient();
        string referer = $"https://blog.naver.com/PostList.naver?blogId={this.blogId}&widgetTypeCall=true&directAccess=true";
        string url = $"https://blog.naver.com/WidgetListAsync.naver?blogId={this.blogId}&listNumVisitor=5&isVisitorOpen=false&isBuddyOpen=false&selectCategoryNo=&skinId=0&skinType=C&isCategoryOpen=true&isEnglish=false&listNumComment=5&areaCode=11B10101&weatherType=0&currencySign=ALL&enableWidgetKeys=title%2Cmenu%2Cprofile%2Ccategory%2Csearch%2Crss%2Ccontent%2Cgnb%2Cexternalwidget&writingMaterialListType=1&calType=";
        httpClient.DefaultRequestHeaders.Add("referer", referer);
        string responseContent = await httpClient.GetStringAsync(url);
        dynamic jsonResponse = JsonConvert.DeserializeObject(responseContent);
        if (jsonResponse != null)
        {
            await Console.Out.WriteLineAsync("Blog Not Found.");
            return;
        }
        string content = "<ul" + ((string)jsonResponse["category"]["content"]).Replace(" \"", "\"").Replace("ul", "`").Split('`')[1] + "ul>";
        var ul = new HtmlDocument();
        ul.LoadHtml(content);
        string numOfPost = ul.DocumentNode.SelectSingleNode("//li[@class='allview']//span").InnerText;

        this.totalPosts = int.Parse(numOfPost.Replace("(", "").Replace(")", ""));
        var lines = ul.DocumentNode.SelectNodes("//a");

        int index = 0;
        foreach (var line in lines)
        {
            int id = int.Parse(line.GetAttributeValue("id", null).Replace("category", ""));
            string name = line.InnerText;
            categories.Add(id, name);
            if (name.Length > 20) name = name.Substring(0, 20) + "~";
            Console.Write($"\r[{++index}/{lines.Count}] category {id}: '{name}'".PadRight(80));
        }

        this.isBlogInfoGathered = true;
    }

    private async Task GetPostsAsync()
    {
        if (!this.isBlogInfoGathered)
        {
            await this.GetBlogInfoAsync();
        }

        Console.Write("2. Gathering posts\n");

        var web = new HtmlWeb();
        var doc = await web.LoadFromWebAsync($"https://blog.naver.com/PostView.naver?blogId={this.blogId}");
        var imgs = doc.DocumentNode.SelectNodes("//div[@id='postViewArea']//img");

        string dbPath = Directory.GetCurrentDirectory() + "db.sqlite";
        if (!File.Exists(dbPath)) SQLiteConnection.CreateFile(dbPath);
        using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
        {
            conn.Open();
            string sql = "CREATE TABLE IF NOT EXISTS posts (blogId TEXT NOT NULL, logNo TEXT PRIMARY KEY, title TEXT NOT NULL, categoryNo INTEGER NOT NULL, parentCategoryNo INTEGER NOT NULL, addDate TEXT NOT NULL)";
            SQLiteCommand command = new SQLiteCommand(sql, conn);
            int result = command.ExecuteNonQuery();

            object lockObj = new object();
            int index = 0;
            int lastPage = (totalPosts / 30) + 1;
            await Parallel.ForEachAsync(Enumerable.Range(1, lastPage), async (currentPage, cancellationToken) =>
            {
                string url = $"https://blog.naver.com/PostTitleListAsync.naver?blogId={blogId}&viewdate=&currentPage={currentPage}&categoryNo=&parentCategoryNo=&countPerPage=30";

                HttpClient httpClient = new HttpClient();
                string responseContent = await httpClient.GetStringAsync(url, cancellationToken);
                dynamic jsonResponse = JsonConvert.DeserializeObject(responseContent);
                if (jsonResponse != null)
                {
                    await Console.Out.WriteLineAsync("Article Not Found.");
                    return;
                }

                lock (lockObj)
                {
                    index++;
                    foreach (dynamic postObj in jsonResponse.postList)
                    {
                        Post post = new Post(blogId, postObj);
                        posts.Add(post);
                        string shortTitle = post.title;
                        if (shortTitle.Length > 20) shortTitle = shortTitle.Substring(0, 20) + "~";
                        Console.Write($"\r[{index}/{lastPage}] '{shortTitle}'".PadRight(80));
                        sql = $"INSERT OR IGNORE INTO posts (blogId, logNo, title, categoryNo, parentCategoryNo, addDate) values ('{post.blogId}', '{post.logNo}', '{post.title}', {post.categoryNo}, {post.parentCategoryNo}, '{post.addDate}')";
                        command = new SQLiteCommand(sql, conn);
                        result = command.ExecuteNonQuery();
                    }
                }
            }
            );
        }
        this.isPostsGathered = true;
    }

    private void GetPostsFromSQL()
    {
        string dbPath = Directory.GetCurrentDirectory() + "db.sqlite";
        if (!File.Exists(dbPath)) return;
        using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
        {
            conn.Open();
            string sql = $"SELECT * FROM posts WHERE blogId = '{this.blogId}'";
            SQLiteCommand command = new SQLiteCommand(sql, conn);
            SQLiteDataReader rdr = command.ExecuteReader();
            int index = 0;
            while (rdr.Read())
            {
                string[] arguments = {
                    (string)rdr["blogId"],
                    (string)rdr["logNo"],
                    (string)rdr["title"],
                    rdr["categoryNo"].ToString(),
                    rdr["parentCategoryNo"].ToString(),
                    (string)rdr["addDate"]
                };
                this.posts[index++] = new Post(arguments);
            }
            rdr.Close();
            if (index != this.totalPosts)
            {
                Console.WriteLine($"Some posts are not archieved. Please call 'GetPostsAsync' function to gather the others.\n({index} of {this.totalPosts} archieved)");
            }
        }
    }

    private string FileEscape(string filename)
    {
        filename = filename.Replace("\\", "").Replace("/", "").Replace(":", "").Replace("*", "").Replace("?", "").Replace("\"", "").Replace("<", "").Replace(">", "").Replace("|", "").TrimStart().TrimEnd();
        while (filename.EndsWith('.'))
        {
            filename = filename.Remove(filename.Length - 1);
        }
        return filename;
    }

    private async Task GetImagesAsync(bool isUpdate)
    {
        if (!this.isPostsGathered)
        {
            await this.GetPostsAsync();
        }

        Console.Write("3. Gathering images from each page\n");

        object lockObj = new object();
        string dbPath = Directory.GetCurrentDirectory() + "db.sqlite";
        using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
        {
            conn.Open();
            string sql = "CREATE TABLE IF NOT EXISTS images (blogId TEXT NOT NULL, logNo TEXT NOT NULL, image TEXT PRIMARY KEY NOT NULL, fname NOT NULL)";
            SQLiteCommand command = new SQLiteCommand(sql, conn);
            int result = command.ExecuteNonQuery();

            sql = $"SELECT * FROM images WHERE blogId='{this.blogId}'";
            command = new SQLiteCommand(sql, conn);
            SQLiteDataReader rdr = command.ExecuteReader();
            List<string> completeList = new List<string>();
            while (rdr.Read())
            {
                if (!isUpdate)
                {
                    string[] image = { (string)rdr["image"], (string)rdr["fname"] };
                    this.images.Add(image);
                }
                completeList.Add((string)rdr["logNo"]);
            }
            rdr.Close();

            int index = 0;
            await Parallel.ForEachAsync(this.posts, async (post, cancellationToken) =>
            {
                if (!completeList.Contains(post.logNo))
                {
                    var web = new HtmlWeb();
                    var doc = await web.LoadFromWebAsync($"https://blog.naver.com/PostView.naver?blogId={post.blogId}&logNo={post.logNo}");
                    var imgs = doc.DocumentNode.SelectNodes("//div[@id='postViewArea']//img");
                    if (imgs == null) { imgs = doc.DocumentNode.SelectNodes("//div[@class='se-main-container']//img"); }
                    if (imgs != null)
                    {
                        lock (lockObj)
                        {
                            int added = 0;
                            foreach (var img in imgs)
                            {
                                string src = img.GetAttributeValue("data-lazy-src", null);
                                if (src != null)
                                {
                                    string path = this.FileEscape(this.blogId) + "/";
                                    if (post.parentCategoryNo != post.categoryNo) { path += this.FileEscape(categories[post.parentCategoryNo]) + "/"; }
                                    if (post.categoryNo > 0) { path += this.FileEscape(categories[post.categoryNo]) + "/"; }
                                    path += this.FileEscape(post.title) + "/";
                                    string fname = path + $"[{++added}] " + HttpUtility.UrlDecode(src.Split('/').Last().Split('?')[0]);
                                    string[] image = { src, fname };
                                    this.images.Add(image);
                                    sql = $"INSERT OR IGNORE INTO images (blogId, logNo, image, fname) values ('{post.blogId}', '{post.logNo}', '{src}', '{fname}')";
                                    command = new SQLiteCommand(sql, conn);
                                    result = command.ExecuteNonQuery();
                                }
                            }
                            string shortTitle = post.title;
                            if (shortTitle.Length > 20) shortTitle = shortTitle.Substring(0, 20) + "~";
                            Console.Write($"\r[{++index}/{this.totalPosts}] {added} images found from '{shortTitle}'".PadRight(80));
                        }
                    }
                    else
                    {
                        lock (lockObj)
                        {
                            string shortTitle = post.title;
                            if (shortTitle.Length > 20) shortTitle = shortTitle.Substring(0, 20) + "~";
                            Console.Write($"\r[{++index}/{this.totalPosts}] images not found from '{shortTitle}'".PadRight(80));
                        }
                    }
                }
                else
                {
                    lock (lockObj)
                    {
                        Console.Write($"\r[{++index}/{this.totalPosts}] post already archieved. skipping process.".PadRight(80));
                    }
                }
            });
        }
        this.isImagesGathered = true;
    }

    private void GetImagesFromSQL()
    {
        string dbPath = Directory.GetCurrentDirectory() + "db.sqlite";
        using (SQLiteConnection conn = new SQLiteConnection($"Data Source={dbPath};Version=3;"))
        {
            conn.Open();
            string sql = $"SELECT * FROM images";
            SQLiteCommand command = new SQLiteCommand(sql, conn);
            SQLiteDataReader rdr = command.ExecuteReader();
            while (rdr.Read())
            {
                string[] image = { (string)rdr["image"], (string)rdr["fname"] };
                this.images.Add(image);
            }
            rdr.Close();
        }
    }

    private async Task DownloadImagesAsync()
    {
        Console.Write("4. Downloading images\n");

        object lockObj = new object();
        int index = 0;
        await Parallel.ForEachAsync(this.images, async (image, CancellationToken) =>
        {
            Directory.CreateDirectory(string.Join("/", image[1].Split('/').SkipLast(1)));
            WebClient webClient = new WebClient();
            await webClient.DownloadFileTaskAsync(new Uri(image[0]), image[1]);
            lock (lockObj)
            {
                string fileName = image[1].Trim();
                if (fileName.Length > 31) { fileName = fileName.Substring(image[1].Length - 25); }
                Console.Write($"\r[{++index}/{this.images.Count}] Download queue pulled. '{fileName}'".PadRight(80));
            }
        });
    }

    public static async Task ArchieveBlog(string blogId)
    {
        Console.Write("----------------------------------------------------------\n");
        Console.Write($"Initializing blog image archieving : {blogId}\n\n");
        Blog blog = new Blog(blogId);


        await blog.GetBlogInfoAsync();
        Console.Write("\ncomplete.\n\n");

        await blog.GetPostsAsync();
        Console.Write("\ncomplete.\n\n");

        await blog.GetImagesAsync(false);
        Console.Write("\ncomplete.\n\n");

        await blog.DownloadImagesAsync();
        Console.Write("\ncomplete.\n\n");


        Console.Write($"Successfully archieved blog '{blogId}'\n");
        Console.Write("----------------------------------------------------------\n\n");
    }

    public static async Task UpdateArchieve(string blogId)
    {
        Console.Write("----------------------------------------------------------\n");
        Console.Write($"Initializing blog image archieving : {blogId}\n\n");
        Blog blog = new Blog(blogId);


        await blog.GetBlogInfoAsync();
        Console.Write("\ncomplete.\n\n");

        await blog.GetPostsAsync();
        Console.Write("\ncomplete.\n\n");

        await blog.GetImagesAsync(true);
        Console.Write("\ncomplete.\n\n");

        await blog.DownloadImagesAsync();
        Console.Write("\ncomplete.\n\n");


        Console.Write($"Successfully archieved blog '{blogId}'\n");
        Console.Write("----------------------------------------------------------\n\n");
    }
}
