﻿using System;
using System.Collections.Generic;

namespace Domain
{
    public interface IQuizApi
    {
        IEnumerable<Topic> Topics { get; }

        Topic this[int topicId] { get; }

        Topic this[string topicName] { get; }

        Guid Id { get; }
    }
}
