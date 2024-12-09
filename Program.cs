using Microsoft.Extensions.Configuration;
using orm1;
using orm1.Models;

namespace CustomORM
{
    public class Program
    {
        public static void Main()
        {
            var appSettingsPath = @"C:\\Users\\vijay\\source\\repos\\orm1\\orm1\\appsettings.json";

            // Build the configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory) // Sets the base path for relative paths (optional but recommended)
                .AddJsonFile(appSettingsPath, optional: false, reloadOnChange: true)
                .Build();

            // Retrieve the connection string from appsettings.json
            string connectionString = configuration.GetConnectionString("DefaultConnection");

            // Initialize ORM and sync database
            ORM.Initialize(connectionString);

            // Perform CRUD operations
            Console.WriteLine("Performing CRUD operations...");

            // Create
            ORM.Insert(new Student { Name = "John Doe", Age = 21 });
            ORM.Insert(new Student { Name = "Jane Smith", Age = 22 });

            // Read
            var students = ORM.GetAll<Student>();
            Console.WriteLine("Students in Database:");
            foreach (var student in students)
            {
                Console.WriteLine($"Id: {student.Id}, Name: {student.Name}, Age: {student.Age}");
            }

            // Update
            var studentToUpdate = students.First();
            studentToUpdate.Name = "Vijay";
            ORM.Update(studentToUpdate);

            // Delete
            var studentToDelete = students.Last();
            ORM.Delete(studentToDelete);

            Console.WriteLine("Final Students in Database:");
            students = ORM.GetAll<Student>();
            foreach (var student in students)
            {
                Console.WriteLine($"Id: {student.Id}, Name: {student.Name}, Age: {student.Age}");
            }
        }
    }
}
