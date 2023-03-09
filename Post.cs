using System.Web;

public class Post
{
    public string blogId;
    public string logNo;
    public string title;
    public int categoryNo;
    public int parentCategoryNo;
    public string addDate;

    public Post(string blogId, dynamic post)
    {
        this.blogId = blogId;
        this.logNo = post.logNo;
        this.title = HttpUtility.UrlDecode(post.title.ToString()).Replace('\'','`');
        this.categoryNo = post.categoryNo;
        this.parentCategoryNo = post.parentCategoryNo;
        this.addDate = post.addDate;
    }

    public Post(string[] arguments)
    {
        this.blogId = arguments[0];
        this.logNo = arguments[1];
        this.title = arguments[2];
        this.categoryNo = int.Parse(arguments[3]);
        this.parentCategoryNo= int.Parse(arguments[4]);
        this.addDate= arguments[5];
    }
}
