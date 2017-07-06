using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DataStreamAPIGenerator.Model
{
    public enum Gender
    {
        Male,
        Female
    }
    public class User
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string City { get; set; }
        public string Country { get; set; }
        public string Mobile { get; set; }
        public DateTime Bithdate { get; set; }
        public Gender Gender { get; set; }

    }
    public class Employee : User
    {
        public Employee(string EmployeeId)
        {
            this.EmployeeId = EmployeeId;
        }
        public string EmployeeId { get; set; }
        public string Email { get; set; }
        public Organization Organization { get; set; }
        public DateTime CreatedOn { get; set; }
    }
    public class ResultBreif
    {
        public int TotalRecords { get; set; }
        public string ReportName { get; set; }
        public string Criteria { get; set; }
    }
}
