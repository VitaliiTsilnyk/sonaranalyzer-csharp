﻿using System;

namespace Tests.TestCases
{
    class CatchRethrow
    {
        private void doSomething(  ) { throw new NotSupportedException()  ; }
        public void Test()
        {
            var someWronglyFormatted =      45     ;

            try
            {
                doSomething();
            }
            catch (Exception exc) //Noncompliant
            {
                throw;
            }

            try
            {
                doSomething();
            }
            catch (ArgumentException) //Noncompliant
            {
                throw;
            }

            try
            {
                doSomething();
            }
            catch (ArgumentException) //Noncompliant
            {
                throw;
            }
            catch (NotSupportedException) //Noncompliant
            {
                throw;
            }

            try
            {
                doSomething();
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch
            {
                Console.WriteLine("");
                throw;
            }

            try
            {
                doSomething();
            }
            catch (ArgumentException) // Noncompliant
            {
                throw;
            }
            catch (NotSupportedException)
            {
                Console.WriteLine("");
                throw;
            }

            try
            {
                doSomething();
            }
            catch (ArgumentException) when (true)
            {
                throw;
            }
            catch (NotSupportedException)
            {
                Console.WriteLine("");
                throw;
            }

            try
            {
                doSomething();
            }
            catch (NotSupportedException) //Noncompliant
            {
                throw;
            }
            finally
            {

            }

            try
            {
                doSomething();
            }
            catch (ArgumentNullException) //Noncompliant
            {
                throw;
            }
            catch (NotImplementedException)
            {
                Console.WriteLine("");
                throw;
            }
            catch (ArgumentException) //Noncompliant
            {
                throw;
            }
            catch //Noncompliant
            {
                throw;
            }

            try
            {
                doSomething();
            }
            catch (ArgumentNullException)
            {
                throw;
            }
            catch (NotImplementedException)
            {
                Console.WriteLine("");
                throw;
            }
            catch (ArgumentException)
            {
                ;
                throw;
            }
            catch(SystemException) //Noncompliant
            {
                throw;
            }
            catch //Noncompliant
            {
                throw;
            }
        }
    }
}
