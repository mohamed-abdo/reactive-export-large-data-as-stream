using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataStreamAPIGenerator.Model;
using Bogus;

namespace DataStreamAPIGenerator.DataRepository
{
    public interface IDataGenerator
    {
        ResultBreif BuildSummary();
        IEnumerable<Employee> BuildEmployeesCopy(int pageSize, int startFrom);
    }
    public class DataGenerator : IDataGenerator, IDisposable
    {
        // multithreading singletone
        private static volatile DataGenerator _instance;
        private volatile IEnumerable<Employee> _employeesLocalCopy;
        private static object syncAccess = new object(), syncEmployees = new object();
        private DataGenerator()
        {

        }
        public static DataGenerator CreateInstance()
        {
            if (_instance == null)
            {
                lock (syncAccess)
                {
                    return new DataGenerator();
                }
            }
            return _instance;
        }
        public ResultBreif BuildSummary()
        {
            var faker = new Faker();
            return new ResultBreif()
            {
                TotalRecords = _employeesLocalCopy?.Count() ?? 0,
                ReportName = $" Report for : {faker.Name.JobTitle()}",
                Criteria = faker.Lorem.Paragraph()
            };
        }
        public IEnumerable<Employee> BuildEmployeesCopy(int pageSize, int startFrom)
        {
            if (_employeesLocalCopy == null)
                lock (syncEmployees)
                {
                    return _employeesLocalCopy = GenerateEmployeesCopy(pageSize, 0);
                }
            return _employeesLocalCopy
                .Skip(startFrom)
                .Take(pageSize);
        }
        private IEnumerable<Employee> GenerateEmployeesCopy(int pageSize, int startFrom)
        {
            return Enumerable.Range(0, pageSize).Select(e =>
            {
                return GenerateEmployee(e);
            });
        }
        private IEnumerable<Employee> YieldEmployeesCopy(int pageSize, int startFrom)
        {
            for (var idx = startFrom; idx < pageSize; idx++)
            {
                yield return GenerateEmployee(idx);
            }
        }
        Func<int, Employee> GenerateEmployee = (idx) =>
        {
            return new Faker<Employee>()
                   .CustomInstantiator(f => { return new Employee(idx.ToString("D10")); })
                   .RuleFor(e => e.Id, f => idx)
                   .RuleFor(e => e.Name, f => f.Name.FirstName())
                   .RuleFor(e => e.Gender, f => f.PickRandom<Gender>())
                   .RuleFor(e => e.Bithdate, f => f.Date.Between(DateTime.Now.AddYears(-60), DateTime.Now.AddYears(-20)))
                   .RuleFor(e => e.City, f => f.Address.City())
                   .RuleFor(e => e.Country, f => f.Address.Country())
                   .RuleFor(e => e.Email, f => f.Internet.Email())
                   .RuleFor(e => e.Mobile, f => f.Phone.PhoneNumber())
                   .RuleFor(e => e.CreatedOn, f => f.Date.Past())
                   .RuleFor(e => e.Organization, f => new Organization()
                   {
                       Id = idx,
                       Name = f.Company.CompanyName(),
                       WebSite = new Uri(f.Internet.Url()),
                       Country = f.Address.Country(),
                       Address = f.Address.FullAddress()
                   }).Generate();
        };
        public void Dispose()
        {
            _instance = null;
        }
    }
}

