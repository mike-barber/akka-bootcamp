﻿using System;
using System.Linq;
using Akka.Actor;
using Akka.Routing;

namespace GithubActors.Actors
{
    /// <summary>
    /// Top-level actor responsible for coordinating and launching repo-processing jobs
    /// </summary>
    public class GithubCommanderActor : ReceiveActor, IWithUnboundedStash
    {
        #region Message classes

        public class CanAcceptJob
        {
            public CanAcceptJob(RepoKey repo)
            {
                Repo = repo;
            }

            public RepoKey Repo { get; private set; }
        }

        public class AbleToAcceptJob
        {
            public AbleToAcceptJob(RepoKey repo)
            {
                Repo = repo;
            }

            public RepoKey Repo { get; private set; }
        }

        public class UnableToAcceptJob
        {
            public UnableToAcceptJob(RepoKey repo)
            {
                Repo = repo;
            }

            public RepoKey Repo { get; private set; }
        }

        #endregion

        private IActorRef _coordinator;
        private IActorRef _canAcceptJobSender;

        public IStash Stash { get; set; }

        private int pendingJobReplies;

        public GithubCommanderActor()
        {
            Ready();
        }

        private void Ready()
        {
            Receive<CanAcceptJob>(job =>
            {
                _coordinator.Tell(job);

                BecomeAsking();
            });
        }

        private void BecomeAsking()
        {
            _canAcceptJobSender = Sender;
            pendingJobReplies = 3; //the number of routees
            Become(Asking);
        }

        private void Asking()
        {
            // stash any subsequent requests
            Receive<CanAcceptJob>(job => Stash.Stash());

            Receive<UnableToAcceptJob>(job =>
            {
                pendingJobReplies--;
                if (pendingJobReplies == 0)
                {
                    _canAcceptJobSender.Tell(job);
                    BecomeReady();
                }
            });

            Receive<AbleToAcceptJob>(job =>
            {
                _canAcceptJobSender.Tell(job);

                // start processing messages
                Sender.Tell(new GithubCoordinatorActor.BeginJob(job.Repo));

                // launch the new window to view results of the processing
                Context.ActorSelection(ActorPaths.MainFormActor.Path).Tell(
                    new MainFormActor.LaunchRepoResultsWindow(job.Repo, Sender));

                BecomeReady();
            });
        }

        private void BecomeReady()
        {
            Become(Ready);
            Stash.UnstashAll();
        }



        protected override void PreStart()
        {
            var instanceNumbers = Enumerable.Range(1, 3).ToArray();

            var coordinatorActors = instanceNumbers
                .Select(i => Context.ActorOf(Props.Create(() => new GithubCoordinatorActor()), 
                    $"{ActorPaths.GithubCoordinatorActor.Name}{i}"))
                .ToArray();

            var paths = instanceNumbers.Select(i => $"{ActorPaths.GithubCoordinatorActor.Path}{i}").ToArray();
            _coordinator = Context.ActorOf(Props.Empty.WithRouter(
                new BroadcastGroup(paths)));

            base.PreStart();
        }
    }
}
