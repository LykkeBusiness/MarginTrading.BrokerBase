## [[tbd]] - [[date]]

### Fixed
- LT-5719: Register poison queue handler in DI

## 8.8.0 - 2024-10-28

### Changed
- LT-5858: Update Lykke core packages

## 8.7.0 - 2024-10-21

### Changed
- LT-5719: bump Lykke.RabbitMqBroker -> 15.4.0

## 8.6.5 - 2024-10-21

### Fixed
- LT-5719: Reuse poison queue handler from Lykke.RabbitMqBroker

## 8.6.4 - 2024-10-17

### Fixed
- LT-5719: Avoid using custom basic properties when publishing messages

## 8.6.2 - 2024-10-17

### Fixed
- LT-5719: Routing key if null causes exception

## 8.6.1 - 2024-10-17

### Fixed
- LT-5719: Poison queues handling when they of quorum type

## 8.6.0 - 2024-10-15

### Added
- LT-5787: Switch assembly logging to hosted service

## 8.5.0 - 2024-10-11

### Added
- LT-5787: Add assembly load logger

## 8.4.0 - 2024-09-26

### Changed
- LT-5719: bump Lykke.RabbitMqBroker -> 15.1.0

## 8.3.0 - 2024-06-12

### Added
- LT-5509: Rabbit MQ listeners can be registered without autostart.

## 8.2.0 - 2024-06-07

### Added
- LT-5509: RabbitMqBroker library version is now a part of connection display name

## 8.1.1 - 2024-06-04

### Fixed
- LT-5509: Register dlx exchange and queue if configured when creating a new RabbitMQ subscriber from template.

## 8.1.0 - 2024-05-30

### Changed
- LT-5509: Switch to template-based RabbiMQ subscribers

## 8.0.2 - 2024-05-29

### Changed
- LT-5509: Objects disposal fixed in `RabbitMqPoisonQueueHandler`

## 8.0.1 - 2024-05-29

### Changed
- LT-5509: Update RabbitMQ broker library with new RabbitMQ.Client and templates

## 7.0.6 - 2023-08-04

### Changed
- LT-5041: Update RabbitMqBroker NuGet package

## 7.0.5 - 2023-08-04

### Changed

- Experimental version to fix build pipeline

## 7.0.4 - 2023-08-04

### Added

- CHANGELOG.md file