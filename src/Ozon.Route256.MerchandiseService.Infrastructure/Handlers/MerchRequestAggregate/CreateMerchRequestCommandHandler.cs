﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Ozon.Route256.MerchandiseService.Domain.AggregateModels.EmployeeAggregate;
using Ozon.Route256.MerchandiseService.Domain.AggregateModels.MerchRequestAggregate;
using Ozon.Route256.MerchandiseService.Domain.AggregateModels.ValueObjects;
using Ozon.Route256.MerchandiseService.Domain.BaseModels;
using Ozon.Route256.MerchandiseService.Domain.Repository;
using Ozon.Route256.MerchandiseService.Infrastructure.Commands;
using Ozon.Route256.MerchandiseService.Infrastructure.Exceptions;
using Ozon.Route256.MerchandiseService.Infrastructure.Integrations.StockApi;

namespace Ozon.Route256.MerchandiseService.Infrastructure.Handlers.MerchRequestAggregate
{
    public class CreateMerchRequestCommandHandler : IRequestHandler<CreateMerchRequestCommand, MerchRequest>
    {
        private readonly IMerchPackItemRepository _merchItemRepository;
        private readonly IMerchRequestRepository _merchRequestRepository;
        private readonly IUnitOfWork _unitOfWork;

        public CreateMerchRequestCommandHandler(IMerchPackItemRepository merchItemRepository,
            IMerchRequestRepository merchRequestRepository, IUnitOfWork unitOfWork)
        {
            _merchItemRepository = merchItemRepository;
            _merchRequestRepository = merchRequestRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<MerchRequest> Handle(CreateMerchRequestCommand request, CancellationToken cancellationToken)
        {
            var employee = new Employee(
                request.EmployeeId,
                Size.Parse(request.Size),
                new Email(request.Email));

            var requestType = MerchRequestType.Parse(request.MerchType);

            var existingMerchRequest =
                await _merchRequestRepository.GetMerchRequestByEmployeeIdAndMerchTypeAsync(employee.Id, requestType,
                    cancellationToken);

            if (existingMerchRequest is not null)
            {
                throw new MerchRequestAlreadyCreatedException(
                    $"Merch request is with type {existingMerchRequest.Type} for employee with id {existingMerchRequest.Employee.Id} already created");
            }

            var merchRequest = await CreateAndFillMerchRequest(requestType, employee, cancellationToken);

            await _unitOfWork.StartTransaction(cancellationToken);

            merchRequest.SetStatusInWork();

            var createResult = await _merchRequestRepository.CreateMerchRequestAsync(merchRequest, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return merchRequest;
        }

        private async Task<MerchRequest> CreateAndFillMerchRequest(MerchRequestType merchRequestType, Employee employee, CancellationToken cancellationToken)
        {
            var merchRequest = new MerchRequest(merchRequestType, employee, DateTime.Now);

            var merchPackItems =
                await _merchItemRepository.CollectItemsByMerchRequestTypeAndSizeAsync(merchRequestType, employee.Size,
                    cancellationToken);
            
            foreach (var item in merchPackItems)
            {
                merchRequest.AddItem(new MerchRequestItem(new Sku(item.Sku.Value),
                    new Quantity(item.Quantity.Value)));
            }

            return merchRequest;
        }
    }
}