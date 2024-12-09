using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace orm1.Models
{
    [Table("Students")]
    public class Student
    {
        [Key]
        public int Id { get; set; }
        public string Name { get; set; }
        public int Age { get; set; }
        public int? Height { get; set; }
        // Foreign Key Reference
        public int? LectureId { get; set; }

        [ForeignKey("LectureId")] // Establish the foreign key relationship
        public Lecture Lecture { get; set; }
    }
}
