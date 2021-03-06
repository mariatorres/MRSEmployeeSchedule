﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MRSES.Core.Entities;
using NpgsqlTypes;
using Npgsql;
using MRSES.Core.Shared;

namespace MRSES.Core.Persistence
{
    public interface ITurnRepository : IDatabase
    {
        Task<List<Schedule>> GetScheduleByPositionAsync(string position, NodaTime.LocalDate ofWeek);
        Task<Schedule> GetEmployeeScheduleAsync(string employeeName, NodaTime.LocalDate ofWeek);
        Task<Dictionary<string, Turn>> GetScheduleByDayAsync(string position, NodaTime.LocalDate date);
        ISchedule Schedule { get; set; }
    }

    public class TurnRepository : ITurnRepository, IDisposable
    {
        IEmployeeRepository _employeeRepository;
        public ISchedule Schedule { get; set; }

        public TurnRepository()
        {
            _employeeRepository = new EmployeeRepository();
        }

        public TurnRepository(ISchedule schedule)
        {
            Schedule = schedule;
        }

        async public Task SaveAsync()
        {
            if (Schedule == null)
                throw new Exception("No se ha establecido horario a guardar.");

            await Task.Run(() => Schedule.Turns.ForEach(async turn => await SaveScheduleAsync(turn)));

            Dispose();
        }

        async Task SaveScheduleAsync(Turn turn)
        {
            using (var dbConnection = new NpgsqlConnection(Configuration.PostgresDbConnection))
            {
                using (var command = new NpgsqlCommand("", dbConnection))
                {
                    command.CommandText = GetQuery("Save");
                    command.CommandType = System.Data.CommandType.Text;
                    command.Parameters.AddWithValue("emp_name", NpgsqlDbType.Varchar, Schedule.Name);
                    command.Parameters.AddWithValue("emp_store", NpgsqlDbType.Varchar, Configuration.StoreLocation);
                    command.Parameters.AddWithValue("turn_date", NpgsqlDbType.Date, DateFunctions.FromLocalDateToDateTime(turn.Date));
                    command.Parameters.AddWithValue("first_turn_in", NpgsqlDbType.Time, DateFunctions.FromLocalTimeToDateTime(turn.TurnIn1));
                    command.Parameters.AddWithValue("first_turn_out", NpgsqlDbType.Time, DateFunctions.FromLocalTimeToDateTime(turn.TurnOut1));
                    command.Parameters.AddWithValue("second_turn_in", NpgsqlDbType.Time, DateFunctions.FromLocalTimeToDateTime(turn.TurnIn2));
                    command.Parameters.AddWithValue("second_turn_out", NpgsqlDbType.Time, DateFunctions.FromLocalTimeToDateTime(turn.TurnOut2));
                    command.Parameters.AddWithValue("of_week", NpgsqlDbType.Date, DateFunctions.FromLocalDateToDateTime(Schedule.OfWeek));

                    await command.Connection.OpenAsync();
                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        static string GetQuery(string action)
        {
            string query = "";
            switch (action)
            {
                case "Save":
                    query = "SELECT add_turn(:emp_name, :emp_store, :turn_date, :first_turn_in, :first_turn_out, :second_turn_in, :second_turn_out, :of_week)";
                    break;
                case "GetEmployeeSchedule":
                    query = "SELECT * FROM get_employee_schedule(:emp_name, :emp_store, :of_week)";
                    break;
                case "GetSingleDaySchedule":
                    query = "SELECT * FROM get_schedule_by_day(:position, :store, :date)";
                    break;
                case "Verify24HourCycle":
                    query = "SELECT violate_cycle_of_24_hours(:emp_name, :emp_store, :turn_date, :first_turn_in, :first_turn_out)";
                    break;
                default:
                    break;
            }

            return query;
        }

        public void Dispose()
        {
            Schedule = null;
        }

        public async Task<List<Schedule>> GetScheduleByPositionAsync(string position, NodaTime.LocalDate ofWeek)
        {
            var employeeNamesByPosition = await _employeeRepository.GetEmployeeNamesByPositionAsync(position);

            if (employeeNamesByPosition.Count == 0)
                throw new Exception("No existen empleados de la posición indicada.");

            var result = await Task.WhenAll(employeeNamesByPosition.Select(async employeeName => await GetEmployeeScheduleAsync(employeeName, ofWeek)));

            return result.ToList();
        }

        public async Task<Schedule> GetEmployeeScheduleAsync(string employeeName, NodaTime.LocalDate ofWeek)
        {
            if (string.IsNullOrEmpty(employeeName) || ofWeek == null)
                throw new Exception("No se ha indicado nombre de empleado o semana");

            var emp_schedule = new Schedule(ofWeek, employeeName);
            int counter = 0;

            using (var dbConnection = new NpgsqlConnection(Configuration.PostgresDbConnection))
            {
                using (var command = new NpgsqlCommand("", dbConnection))
                {
                    command.CommandText = GetQuery("GetEmployeeSchedule");
                    command.CommandType = System.Data.CommandType.Text;
                    command.Parameters.AddWithValue("emp_name", NpgsqlDbType.Varchar, employeeName);
                    command.Parameters.AddWithValue("emp_store", NpgsqlDbType.Varchar, Configuration.StoreLocation);
                    command.Parameters.AddWithValue("of_week", NpgsqlDbType.Date, DateFunctions.FromLocalDateToDateTime(ofWeek));

                    await command.Connection.OpenAsync();

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (!(await reader.ReadAsync()))
                            return emp_schedule;

                        do
                        {
                            var turn_in_1 = await reader.GetFieldValueAsync<DateTime>(1);
                            var turn_out_1 = await reader.GetFieldValueAsync<DateTime>(2);
                            var turn_in_2 = await reader.GetFieldValueAsync<DateTime>(3);
                            var turn_out_2 = await reader.GetFieldValueAsync<DateTime>(4);

                            emp_schedule.Turns[counter].TurnIn1 = DateFunctions.FromDateTimeToLocalTime(turn_in_1);
                            emp_schedule.Turns[counter].TurnOut1 = DateFunctions.FromDateTimeToLocalTime(turn_out_1);
                            emp_schedule.Turns[counter].TurnIn2 = DateFunctions.FromDateTimeToLocalTime(turn_in_2);
                            emp_schedule.Turns[counter].TurnOut2 = DateFunctions.FromDateTimeToLocalTime(turn_out_2);

                        } while (await reader.ReadAsync() && ++counter < 7);
                    }
                }
            }

            return emp_schedule;
        }

        public async Task<Dictionary<string, Turn>> GetScheduleByDayAsync(string position, NodaTime.LocalDate date)
        {
            if (string.IsNullOrEmpty(position) || date == null)
                throw new Exception("No se ha indicado posición o dia");

            var singleDaySchedule = new Dictionary<string, Turn>();

            using (var dbConnection = new NpgsqlConnection(Configuration.PostgresDbConnection))
            {
                using (var command = new NpgsqlCommand("", dbConnection))
                {
                    command.CommandText = GetQuery("GetSingleDaySchedule");
                    command.CommandType = System.Data.CommandType.Text;
                    command.Parameters.AddWithValue("position", NpgsqlDbType.Varchar, position);
                    command.Parameters.AddWithValue("store", NpgsqlDbType.Varchar, Configuration.StoreLocation);
                    command.Parameters.AddWithValue("date", NpgsqlDbType.Date, DateFunctions.FromLocalDateToDateTime(date));

                    await command.Connection.OpenAsync();

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (!(await reader.ReadAsync()))
                            return singleDaySchedule;

                        do
                        {
                            var empName = await reader.GetFieldValueAsync<string>(0);
                            var firstTurnIn = await reader.GetFieldValueAsync<DateTime>(1);
                            var firstTurnOut = await reader.GetFieldValueAsync<DateTime>(2);
                            var secondTurnIn = await reader.GetFieldValueAsync<DateTime>(3);
                            var secondTurnOut = await reader.GetFieldValueAsync<DateTime>(4); 

                            singleDaySchedule.Add(empName,
                                new Turn(date, DateFunctions.FromDateTimeToLocalTime(firstTurnIn), 
                                    DateFunctions.FromDateTimeToLocalTime(firstTurnOut),
                                    DateFunctions.FromDateTimeToLocalTime(secondTurnIn),
                                    DateFunctions.FromDateTimeToLocalTime(secondTurnOut)));
                            

                        } while (await reader.ReadAsync());
                    }
                }
            }

            return singleDaySchedule;
        }

        public static async Task<bool> ViolateCycleOfTwentyFourHour(string emp_name, ITurn turn)
        {
            bool result = true;

            using (var dbConnection = new NpgsqlConnection(Configuration.PostgresDbConnection))
            {
                using (var command = new NpgsqlCommand("", dbConnection))
                {
                    command.CommandText = GetQuery("Verify24HourCycle");
                    command.CommandType = System.Data.CommandType.Text;
                    command.Parameters.AddWithValue("emp_name", NpgsqlDbType.Varchar, emp_name);
                    command.Parameters.AddWithValue("emp_store", NpgsqlDbType.Varchar, Configuration.StoreLocation);
                    command.Parameters.AddWithValue("turn_date", NpgsqlDbType.Date, DateFunctions.FromLocalDateToDateTime(turn.Date));
                    command.Parameters.AddWithValue("first_turn_in", NpgsqlDbType.Time, DateFunctions.FromLocalTimeToDateTime(turn.TurnIn1));
                    command.Parameters.AddWithValue("first_turn_out", NpgsqlDbType.Time, DateFunctions.FromLocalTimeToDateTime(turn.TurnOut1));

                    await command.Connection.OpenAsync();

                    using (var reader = await command.ExecuteReaderAsync())
                        if (await reader.ReadAsync())
                            result = await reader.GetFieldValueAsync<bool>(0);
         
                }
            }

            return result;
        }
    }
}
