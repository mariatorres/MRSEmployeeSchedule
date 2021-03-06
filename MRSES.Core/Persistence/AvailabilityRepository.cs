﻿using MRSES.Core.Entities;
using MRSES.Core.Shared;
using Npgsql;
using NpgsqlTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MRSES.Core.Persistence
{
    public interface IAvailabilityRepository
    {
        System.Threading.Tasks.Task SaveAsync();
        System.Threading.Tasks.Task<Availability> GetAvailabilityAsync();
        System.Threading.Tasks.Task<string> GetAvailabilityOfADayAsync(NodaTime.IsoDayOfWeek dayOfWeek);
    }

    public class AvailabilityRepository : IAvailabilityRepository, IDatabase, IDisposable
    {
        #region Properties

        public IAvailability Availability { get; set; }
        public string EmployeeName { get; set; }

        #endregion

        #region Constructors

        public AvailabilityRepository() { }

        public AvailabilityRepository(string employeeName, IAvailability availability)
        {
            Availability = availability;
            EmployeeName = employeeName;
        }

        #endregion

        #region Methods

        async public Task SaveAsync()
        {
            CheckIfEmployeeNameIsProvided();

            if (Availability == null)
                throw new Exception("No se ha indicado la disponibilidad del empleado.");

            using (var dbConnection = new NpgsqlConnection(Configuration.PostgresDbConnection))
            {
                using (var command = new NpgsqlCommand("", dbConnection))
                {
                    command.CommandText = GetQuery("SaveAvailability");
                    command.CommandType = System.Data.CommandType.Text;
                    command.Parameters.AddWithValue("emp_name", NpgsqlDbType.Varchar, EmployeeName);
                    command.Parameters.AddWithValue("emp_store", NpgsqlDbType.Varchar, Configuration.StoreLocation);
                    command.Parameters.AddWithValue("monday", NpgsqlDbType.Varchar, Availability.Monday);
                    command.Parameters.AddWithValue("tuesday", NpgsqlDbType.Varchar, Availability.Tuesday);
                    command.Parameters.AddWithValue("wednesday", NpgsqlDbType.Varchar, Availability.Wednesday);
                    command.Parameters.AddWithValue("thursday", NpgsqlDbType.Varchar, Availability.Thursday);
                    command.Parameters.AddWithValue("friday", NpgsqlDbType.Varchar, Availability.Friday);
                    command.Parameters.AddWithValue("saturday", NpgsqlDbType.Varchar, Availability.Saturday);
                    command.Parameters.AddWithValue("sunday", NpgsqlDbType.Varchar, Availability.Sunday);

                    await command.Connection.OpenAsync();
                    await command.ExecuteNonQueryAsync();
                }
            }

            Dispose();
        }

        void CheckIfEmployeeNameIsProvided()
        {
            if (string.IsNullOrEmpty(EmployeeName))
                throw new Exception("No se ha especificado el nombre del empleado");
        }

        async public Task<Availability> GetAvailabilityAsync()
        {
            CheckIfEmployeeNameIsProvided();
            var employeeAvailability = new Availability();

            using (var dbConnection = new NpgsqlConnection(Configuration.PostgresDbConnection))
            {
                using (var command = new NpgsqlCommand("", dbConnection))
                {
                    command.CommandText = GetQuery("GetAvailability");
                    command.CommandType = System.Data.CommandType.Text;
                    command.Parameters.AddWithValue("employee_store", NpgsqlDbType.Varchar, Configuration.StoreLocation);
                    command.Parameters.AddWithValue("employee_name", NpgsqlDbType.Varchar, EmployeeName);

                    await command.Connection.OpenAsync();

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            employeeAvailability.Monday = await reader.GetFieldValueAsync<string>(0);
                            employeeAvailability.Tuesday = await reader.GetFieldValueAsync<string>(1);
                            employeeAvailability.Wednesday = await reader.GetFieldValueAsync<string>(2);
                            employeeAvailability.Thursday = await reader.GetFieldValueAsync<string>(3);
                            employeeAvailability.Friday = await reader.GetFieldValueAsync<string>(4);
                            employeeAvailability.Saturday = await reader.GetFieldValueAsync<string>(5);
                            employeeAvailability.Sunday = await reader.GetFieldValueAsync<string>(6);
                        }
                    }
                }
            }

            return employeeAvailability;
        }

        async public Task<string> GetAvailabilityOfADayAsync(NodaTime.IsoDayOfWeek dayOfWeek)
        {
            string result = "";

            var employeeAvailability = await GetAvailabilityAsync();

            switch (dayOfWeek)
            {
                case NodaTime.IsoDayOfWeek.Friday:
                    result = employeeAvailability.Friday;
                    break;
                case NodaTime.IsoDayOfWeek.Monday:
                    result = employeeAvailability.Monday;
                    break;
                case NodaTime.IsoDayOfWeek.Saturday:
                    result = employeeAvailability.Saturday;
                    break;
                case NodaTime.IsoDayOfWeek.Sunday:
                    result = employeeAvailability.Sunday;
                    break;
                case NodaTime.IsoDayOfWeek.Thursday:
                    result = employeeAvailability.Thursday;
                    break;
                case NodaTime.IsoDayOfWeek.Tuesday:
                    result = employeeAvailability.Tuesday;
                    break;
                case NodaTime.IsoDayOfWeek.Wednesday:
                    result = employeeAvailability.Wednesday;
                    break;
                default:
                    break;
            }

            return result;
        }

        public static async Task<bool> CanDoTheTurnAsync(string employeeName, ITurn turn)
        {
            bool result = true;
            var employeeId = await EmployeeRepository.GetEmployeeIdAsync(employeeName);

            using (var dbConnection = new NpgsqlConnection(Configuration.PostgresDbConnection))
            {
                using (var command = new NpgsqlCommand("", dbConnection))
                {
                    command.CommandText = GetQuery("VerifyAvailability");
                    command.CommandType = System.Data.CommandType.Text;
                    command.Parameters.AddWithValue("emp_id", NpgsqlDbType.Varchar, employeeId);
                    command.Parameters.AddWithValue("turn_date", NpgsqlDbType.Date, DateFunctions.FromLocalDateToDateTime(turn.Date));
                    command.Parameters.AddWithValue("turn_in1", NpgsqlDbType.Time, DateFunctions.FromLocalTimeToDateTime(turn.TurnIn1));
                    command.Parameters.AddWithValue("turn_out1", NpgsqlDbType.Time, DateFunctions.FromLocalTimeToDateTime(turn.TurnOut1));
                    command.Parameters.AddWithValue("turn_in2", NpgsqlDbType.Time, DateFunctions.FromLocalTimeToDateTime(turn.TurnIn2));
                    command.Parameters.AddWithValue("turn_out2", NpgsqlDbType.Time, DateFunctions.FromLocalTimeToDateTime(turn.TurnOut2));

                    await command.Connection.OpenAsync();

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            result = await reader.GetFieldValueAsync<bool>(0);
                        }
                    }
                }
            }

            return result;
        }

        static string GetQuery(string action)
        {
            string query = string.Empty;
            switch (action)
            {
                case "GetAvailability":
                    query = "SELECT * FROM get_availability(:employee_name, :employee_store)";
                    break;
                case "SaveAvailability":
                    query = "SELECT add_availability(:emp_name, :emp_store, :monday, :tuesday, :wednesday, :thursday, :friday, :saturday, :sunday)";
                    break;
                case "VerifyAvailability":
                    query = "SELECT availability_employee_can_do_the_turn(:emp_id, :turn_date, :turn_in1, :turn_out1, :turn_in2, :turn_out2)";
                    break;
                default:
                    break;
            }

            return query;
        }

        #endregion
      
        public void Dispose()
        {
            EmployeeName = null;
            Availability = null;
        }
    }
}
