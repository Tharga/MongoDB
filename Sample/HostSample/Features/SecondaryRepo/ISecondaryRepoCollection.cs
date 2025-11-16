using HostSample.Features.DiskRepo;
using Tharga.MongoDB;

namespace HostSample.Features.SecondaryRepo;

public interface ISecondaryRepoCollection : IDiskRepositoryCollection<WeatherForecast>;