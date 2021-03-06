﻿using System;
using System.Collections.Generic;
using System.Linq;
using Application.Exceptions;
using Application.Extensions;
using Application.Info;
using Application.Repositories;
using Application.Repositories.Entities;
using Application.Selectors;
using Domain.Values;
using Infrastructure.Extensions;
using Infrastructure.Result;
using Microsoft.Extensions.Logging;

namespace Application.QuizService
{
    public class QuizService : IQuizService
    {
        private readonly ITaskGeneratorSelector generatorSelector;
        private readonly Random random;

        private readonly ITaskRepository taskRepository;

        private readonly IUserRepository userRepository;

        public QuizService(
            IUserRepository userRepository,
            ITaskRepository taskRepository,
            ITaskGeneratorSelector generatorSelector,
            ILogger<QuizService> logger,
            Random random)
        {
            this.userRepository = userRepository;
            this.taskRepository = taskRepository;
            this.generatorSelector = generatorSelector;
            this.random = random;
            Logger = logger;
            this.random = random;
        }

        private ILogger<QuizService> Logger { get; }

        /// <inheritdoc />
        public Result<IEnumerable<TopicInfo>, Exception> GetTopicsInfo()
        {
            Logger.LogInformation("Showing topics;");

            return taskRepository
                .GetTopics()
                .Select(topic => topic.ToInfo())
                .LogInfo(ts => $"Found {ts.Count()} topics", Logger)
                .Ok();
        }

        /// <inheritdoc />
        public Result<IEnumerable<LevelInfo>, Exception> GetLevels(Guid topicId)
        {
            Logger.LogInformation($"Getting levels for topic {topicId}");

            if (taskRepository.TopicExists(topicId))
                return taskRepository
                    .GetLevelsFromTopic(topicId)
                    .Select(level => level.ToInfo())
                    .LogInfo(levels => $"Found {levels.Count()} levels", Logger)
                    .Ok();

            Logger.LogError($"Did not find any levels for {topicId}");
            return new ArgumentException(nameof(topicId));
        }

        /// <inheritdoc />
        public Result<IEnumerable<LevelInfo>, Exception> GetAvailableLevels(Guid userId, Guid topicId)
        {
            Logger.LogInformation($"Showing Available levels for User @ {userId} at Topic @ {topicId}");

            var user = userRepository.FindOrInsertUser(userId, taskRepository);

            if (!taskRepository.TopicExists(topicId))
                return new ArgumentException(nameof(topicId));

            userRepository.UpdateUserProgress(taskRepository, user);
            userRepository.UpdateTopicProgress(taskRepository, user, topicId);

            return user
                .UserProgressEntity
                .TopicsProgress[topicId]
                .LogInfo(topicProgress => $"Found TopicProgressEntity {topicProgress}", Logger)
                .LevelProgressEntities
                .Select(pair => taskRepository.FindLevel(topicId, pair.Key)?.ToInfo())
                .Where(level => level != null)
                .LogInfo(s => $"Found {s.Count()} levels", Logger)
                .Ok();
        }

        /// <inheritdoc />
        public Result<LevelProgressInfo, Exception> GetProgress(Guid userId, Guid topicId, Guid levelId)
        {
            if (!taskRepository.TopicExists(topicId))
                return new ArgumentException(nameof(topicId));
            if (!taskRepository.LevelExists(topicId, levelId))
                return new ArgumentException(nameof(levelId));

            var user = userRepository.FindOrInsertUser(userId, taskRepository);

            userRepository.UpdateUserProgress(taskRepository, user);
            userRepository.UpdateTopicProgress(taskRepository, user, topicId);
            userRepository.UpdateLevelProgress(taskRepository, user, topicId, levelId);

            var levelsProgress = user
                .UserProgressEntity
                .TopicsProgress[topicId]
                .LevelProgressEntities;

            if (!levelsProgress.ContainsKey(levelId))
                return new AccessDeniedException(
                    $"User {userId} doesn't have access to level {levelId} in topic {topicId}");

            return GetLevelProgress(user, topicId, levelId);
        }

        /// <inheritdoc />
        public Result<TaskInfo, Exception> GetTask(Guid userId, Guid topicId, Guid levelId)
        {
            if (!taskRepository.TopicExists(topicId))
                return new ArgumentException(nameof(topicId))
                    .LogError(_ =>
                            $"No topics exists for {(nameof(userId), userId, nameof(topicId), topicId, nameof(levelId), levelId)}",
                        Logger);
            if (!taskRepository.LevelExists(topicId, levelId))
                return new ArgumentException(nameof(levelId));

            var user = userRepository.FindOrInsertUser(userId, taskRepository);
            var levels = GetAvailableLevels(userId, topicId).Value;

            if (!levels.Select(info => info.Id).Contains(levelId))
                return new
                    AccessDeniedException($"User {userId} doesn't have access to level {levelId} in topic {topicId}");

            userRepository.UpdateLevelProgress(taskRepository, user, topicId, levelId);

            var streaks = user
                .UserProgressEntity
                .TopicsProgress[topicId]
                .LevelProgressEntities[levelId]
                .CurrentLevelStreaks;

            var (_, isFailure, generator, error) = generatorSelector
                .SelectGenerator(taskRepository
                    .GetGeneratorsFromLevel(topicId, levelId), streaks);

            if (isFailure)
                return error;

            var task = generator.GetTask(random);
            UpdateUserCurrentTask(user, topicId, levelId, task);
            return task.ToInfo();
        }

        /// <inheritdoc />
        public Result<TaskInfo, Exception> GetNextTask(Guid userId)
        {
            var user = userRepository.FindOrInsertUser(userId, taskRepository);

            if (!user.HasCurrentTask())
                return new AccessDeniedException($"User {userId} hadn't started any task");

            return GetTask(userId, user.UserProgressEntity.CurrentTopicId, user.UserProgressEntity.CurrentLevelId);
        }

        /// <inheritdoc />
        public Result<bool, Exception> CheckAnswer(Guid userId, string answer)
        {
            var user = userRepository.FindOrInsertUser(userId, taskRepository);
            Logger.LogInformation($"Checking answer for User {user}: his answer is {answer}");
            if (!user.HasCurrentTask())
                return new AccessDeniedException($"User {userId} hadn't started any task");

            var userUserProgress = user.UserProgressEntity;
            var currentTask = userUserProgress.CurrentTask;
            Logger.LogInformation($"User's current task is {currentTask}");

            if (currentTask.IsSolved)
                return new AccessDeniedException("User's current task is solved already");

            if (currentTask.Answer != answer)
            {
                user = GetUserWithNewStreakIfNotSolved(user, _ => 0);
                userRepository.Update(user);
                return false;
            }

            user = user.With(
                userUserProgress.With(
                    currentTask: currentTask.With(isSolved: true)));
            user = GetUserWithNewStreakIfNotSolved(user, streak => streak + 1);
            user = GetUserWithNewProgressIfLevelSolved(user);
            userRepository.Update(user);
            return true;
        }

        /// <inheritdoc />
        public Result<HintInfo, Exception> GetHint(Guid userId)
        {
            var user = userRepository.FindOrInsertUser(userId, taskRepository);

            if (!user.HasCurrentTask())
                return new AccessDeniedException($"User {userId} had not started any task");

            var userProgress = user.UserProgressEntity;
            var currentTask = userProgress.CurrentTask;
            var hints = currentTask.Hints;
            var currentHintIndex = currentTask.HintsTaken;

            if (currentHintIndex >= hints.Length)
                return new OutOfHintsException("Out of hints");

            user = user.With(userProgress.With(currentTask: currentTask.With(hintsTaken: currentTask.HintsTaken + 1)));
            userRepository.Update(user);
            return new HintInfo(hints[currentHintIndex], currentHintIndex < hints.Length - 1);
        }

        private UserEntity GetUserWithNewProgressIfLevelSolved(UserEntity user)
        {
            var topicId = user.UserProgressEntity.CurrentTopicId;
            var levelId = user.UserProgressEntity.CurrentLevelId;
            var progress = GetLevelProgress(user, topicId, levelId);
            if (progress.TasksSolved < progress.TasksCount)
                return user;

            taskRepository
                .FindLevel(topicId, levelId)
                .NextLevels
                .Select(id => taskRepository.FindLevel(topicId, id))
                .ToList()
                .ForEach(level => user
                    .UserProgressEntity
                    .TopicsProgress[topicId]
                    .LevelProgressEntities
                    .TryAdd(level.Id, level.ToProgressEntity()));

            return user;
        }

        private bool IsGeneratorSolved(UserEntity user, Guid topicId, Guid levelId, Guid generatorId)
        {
            var currentStreak = user.GetCurrentStreak(topicId, levelId, generatorId);
            var streakToSolve = taskRepository.FindGenerator(topicId, levelId, generatorId).Streak;
            return currentStreak >= streakToSolve;
        }

        private void UpdateUserCurrentTask(UserEntity user, Guid topicId, Guid levelId, Task task)
        {
            var taskInfoEntity = task.AsInfoEntity();
            var progress = user
                .UserProgressEntity
                .With(topicId, levelId, currentTask: taskInfoEntity);
            user = user.With(progress);
            userRepository.Update(user);
        }

        private UserEntity GetUserWithNewStreakIfNotSolved(UserEntity user, Func<int, int> updateFunc)
        {
            var topicId = user.UserProgressEntity.CurrentTopicId;
            var levelId = user.UserProgressEntity.CurrentLevelId;
            var generatorId = user.UserProgressEntity.CurrentTask.ParentGeneratorId;
            var currentStreak = user.GetCurrentStreak();
            if (!IsGeneratorSolved(user, topicId, levelId, generatorId))
                user.UserProgressEntity
                    .TopicsProgress[topicId]
                    .LevelProgressEntities[levelId]
                    .CurrentLevelStreaks[generatorId] = updateFunc(currentStreak);
            return user;
        }

        private LevelProgressInfo GetLevelProgress(UserEntity user, Guid topicId, Guid levelId)
        {
            var streaks = user
                .UserProgressEntity
                .TopicsProgress[topicId]
                .LevelProgressEntities[levelId]
                .CurrentLevelStreaks;

            var solved = streaks.Sum(pair => pair.Value);
            var total = streaks.Sum(pair => taskRepository.FindGenerator(topicId, levelId, pair.Key).Streak);
            return new LevelProgressInfo(total, solved);
        }
    }
}