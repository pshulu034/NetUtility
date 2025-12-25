using NetUtility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EthernetUtilityDemo
{
    // Define data models for JSONPlaceholder API
    public class Post
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
    }

    public class THttpManager
    {
        public static async Task Test()
        {
            Console.WriteLine("Starting HttpManager Demo...");
            Console.WriteLine("Using JSONPlaceholder API (https://jsonplaceholder.typicode.com)");
            Console.WriteLine("------------------------------------------------------------");

            // Initialize HttpManager
            string baseUrl = "https://jsonplaceholder.typicode.com";
            using var httpManager = new NetUtility.HttpClientEx(baseUrl);

            try
            {
                // 1. GET Request
                Console.WriteLine("\n[1] Testing GET request (Fetch Post ID 1)...");
                var post = await httpManager.GetAsync<Post>("posts/1");
                Console.WriteLine($"Success! Title: {post.Title}");

                // 2. GET Request with Query Params
                Console.WriteLine("\n[2] Testing GET request with Query Params (Fetch posts for User ID 1)...");
                var queryParams = new Dictionary<string, string> { { "userId", "1" } };
                var posts = await httpManager.GetAsync<List<Post>>("posts", queryParams);
                Console.WriteLine($"Success! Retrieved {posts.Count} posts.");

                // 3. POST Request
                Console.WriteLine("\n[3] Testing POST request (Create new post)...");
                var newPost = new Post
                {
                    UserId = 1,
                    Title = "Trae AI Demo",
                    Body = "This is a test post created via HttpManager."
                };
                var createdPost = await httpManager.PostAsync<Post, Post>("posts", newPost);
                Console.WriteLine($"Success! Created Post ID: {createdPost.Id}, Title: {createdPost.Title}");

                // 4. PUT Request
                Console.WriteLine("\n[4] Testing PUT request (Update Post ID 1)...");
                var updatePost = new Post
                {
                    Id = 1,
                    UserId = 1,
                    Title = "Updated Title",
                    Body = "Updated body content."
                };
                var updatedPost = await httpManager.PutAsync<Post, Post>("posts/1", updatePost);
                Console.WriteLine($"Success! Updated Title: {updatedPost.Title}");

                // 5. DELETE Request
                Console.WriteLine("\n[5] Testing DELETE request (Delete Post ID 1)...");
                await httpManager.DeleteAsync("posts/1");
                Console.WriteLine("Success! Post deleted.");

                // 6. Custom Headers
                Console.WriteLine("\n[6] Testing Custom Headers...");
                httpManager.SetHeader("X-Custom-Header", "TraeDemo");
                // Just making a simple request to verify no error
                await httpManager.GetAsync<Post>("posts/1");
                Console.WriteLine("Success! Request with custom header completed.");

            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[!] Error occurred: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }
            }

            Console.WriteLine("\n------------------------------------------------------------");
            Console.WriteLine("Demo completed. Press any key to exit.");
            Console.ReadKey();
        }
    }
}
