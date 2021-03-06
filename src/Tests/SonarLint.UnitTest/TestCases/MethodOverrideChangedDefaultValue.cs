﻿using System;
using System.Collections.Generic;

namespace Tests.Diagnostics
{
    public interface IMyInterface
    {
        void Write(int i, int j = 5);
    }

    public class Base : IMyInterface
    {
        public virtual void Write(int i, int j = 0) // Noncompliant
        {
            Console.WriteLine(i);
        }

        public virtual void Write2()
        {
        }
    }

    public class Derived1 : Base
    {
        public override void Write(int i,
            int j = 42) // Noncompliant
        {
            Console.WriteLine(i);
        }

        public override void Write2(int i)
        {
        }
    }
    public class Derived2 : Base
    {
        public override void Write(int i,
            int j) // Noncompliant
        {
            Console.WriteLine(i);
        }
    }
    public class Derived3 : Base
    {
        public override void Write(int i = 5,  // Noncompliant
            int j = 0)
        {
            Console.WriteLine(i);
        }
    }
}
