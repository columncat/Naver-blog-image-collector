Console.WriteLine("Input blogId(s) separated by line to collect.\nType 'exit' to start download.");

List<string> blogs = new List<string>();
string input = "";
do
{
    input = Console.ReadLine();
    if (input != "exit")
        blogs.Add(input);
} while (input != "exit");

foreach(string blog in blogs)
{
    await Blog.UpdateArchieve(blog);
}

Console.WriteLine("Queue finished.\nPress enter to exit...");
Console.ReadLine();