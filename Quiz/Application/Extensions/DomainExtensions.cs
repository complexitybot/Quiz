﻿using System;
using System.Linq;
using Application.Info;
using Application.Repositories.Entities;
using Domain.Entities;
using Domain.Values;
using Infrastructure.Extensions;

namespace Application.Extensions
{
    public static class DomainExtensions
    {
        public static TopicInfo ToInfo(this Topic topic)
        {
            return new TopicInfo(topic.Name, topic.Id);
        }


        public static LevelInfo ToInfo(this Level level)
        {
            return new LevelInfo(level.Id, level.Description);
        }

        public static TaskInfo ToInfo(this Task task)
        {
            return new TaskInfo(task.Question, task.PossibleAnswers, task.Hints.Length > 0, task.Text);
        }

        public static TaskInfoEntity AsInfoEntity(this Task task)
        {
            return new TaskInfoEntity(
                task.Text,
                task.Answer,
                task.Hints,
                0,
                task.ParentGeneratorId,
                false,
                Guid.NewGuid());
        }

        public static LevelProgressEntity ToProgressEntity(this Level level)
        {
            return new LevelProgressEntity(
                level.Id,
                level.Generators
                    .SafeToDictionary(generator => generator.Id, generator => 0),
                Guid.NewGuid());
        }

        public static TopicProgressEntity ToProgressEntity(this Topic topic)
        {
            return new TopicProgressEntity(
                topic.Levels
                    .Take(1)
                    .SafeToDictionary(level => level.Id, level => level.ToProgressEntity()),
                topic.Id,
                Guid.NewGuid());
        }
    }
}