using MediatR;

namespace Pam.Shared.Contracts.CQRS;

public interface IQuery<out TResponse> : IRequest<TResponse>;
